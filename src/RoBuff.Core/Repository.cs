using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace RoBuff.Core;

public sealed class Repository : IDisposable
{
    private readonly SqliteConnection db;
    public string Folder { get; }
    public Repository(string folder)
    {
        Folder = folder;
        Directory.CreateDirectory(folder);
        db = new(new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, "buffs.db") }.ToString());
        db.Open();
        Exec("PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL;");
        Exec("""
          CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY,value TEXT NOT NULL);
          CREATE TABLE IF NOT EXISTS definitions(id INTEGER PRIMARY KEY,json TEXT NOT NULL);
          CREATE TABLE IF NOT EXISTS discoveries(id INTEGER PRIMARY KEY,count INTEGER NOT NULL,first TEXT NOT NULL,last TEXT NOT NULL,total INTEGER,leftms INTEGER);
          CREATE TABLE IF NOT EXISTS profiles(id TEXT PRIMARY KEY,label TEXT NOT NULL,process TEXT NOT NULL);
          CREATE TABLE IF NOT EXISTS rules(profile TEXT NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,status INTEGER NOT NULL,json TEXT NOT NULL,PRIMARY KEY(profile,status));
          INSERT OR IGNORE INTO meta VALUES('schema','1');
          """);
        foreach (var b in new[] { new BuffDefinition(10, "블레싱", Source: "builtin"), new BuffDefinition(12, "민첩성 증가", Source: "builtin") })
            Exec("INSERT OR IGNORE INTO definitions VALUES($id,$json)", ("$id", b.StatusId), ("$json", JsonSerializer.Serialize(b, DataFormat.Json)));
    }
    private void Exec(string sql, params (string, object?)[] values)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = sql;
        foreach (var (key, value) in values) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    public List<BuffDefinition> Definitions()
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT json FROM definitions ORDER BY id";
        using var r = cmd.ExecuteReader(); List<BuffDefinition> list = [];
        while (r.Read()) list.Add(JsonSerializer.Deserialize<BuffDefinition>(r.GetString(0), DataFormat.Json)!);
        return list;
    }
    public void Save(BuffDefinition buff, bool backup = true)
    {
        DataFormat.Validate(buff);
        if (backup) Backup();
        Exec("INSERT INTO definitions VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json", ("$id", buff.StatusId), ("$json", JsonSerializer.Serialize(buff with { Source = "user" }, DataFormat.Json)));
    }
    public void Observe(StatusEvent e)
    {
        if (!e.Active) return;
        Exec("""
          INSERT INTO discoveries VALUES($id,1,$time,$time,$total,$left)
          ON CONFLICT(id) DO UPDATE SET count=count+1,last=$time,total=$total,leftms=$left
          """, ("$id", e.StatusId), ("$time", e.Time.ToString("O")), ("$total", e.TotalMs), ("$left", e.LeftMs));
    }
    public List<Discovery> Discoveries()
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id,count,first,last,total,leftms FROM discoveries ORDER BY last DESC";
        using var r = cmd.ExecuteReader(); List<Discovery> result = [];
        while (r.Read()) result.Add(new(r.GetInt32(0), r.GetInt32(1), DateTimeOffset.Parse(r.GetString(2)), DateTimeOffset.Parse(r.GetString(3)), r.IsDBNull(4) ? null : (uint)r.GetInt64(4), r.IsDBNull(5) ? null : (uint)r.GetInt64(5)));
        return result;
    }
    public List<Profile> Profiles()
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id,label,process FROM profiles";
        using var r = cmd.ExecuteReader(); List<Profile> result = [];
        while (r.Read()) result.Add(new(r.GetString(0), r.GetString(1), r.GetString(2)));
        return result;
    }
    public void SaveProfile(Profile p) => Exec("INSERT INTO profiles VALUES($id,$label,$process) ON CONFLICT(id) DO UPDATE SET label=$label,process=$process", ("$id", p.Id), ("$label", p.Label), ("$process", p.ProcessName));
    public void DeleteProfile(string id) => Exec("DELETE FROM profiles WHERE id=$id", ("$id", id));
    public CardRule? Rule(string profile, int status)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT json FROM rules WHERE profile=$p AND status=$s";
        cmd.Parameters.AddWithValue("$p", profile); cmd.Parameters.AddWithValue("$s", status);
        return cmd.ExecuteScalar() is string text ? JsonSerializer.Deserialize<CardRule>(text, DataFormat.Json) : null;
    }
    public void SaveRule(CardRule rule) => Exec("INSERT INTO rules VALUES($p,$s,$j) ON CONFLICT(profile,status) DO UPDATE SET json=$j", ("$p", rule.ProfileId), ("$s", rule.StatusId), ("$j", JsonSerializer.Serialize(rule, DataFormat.Json)));
    public void Export(string path) => File.WriteAllText(path, JsonSerializer.Serialize(new DictionaryFile(1, DataFormat.Scope, Definitions()), DataFormat.Json));
    public DictionaryFile ReadImport(string path)
    {
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("사전 파일은 2MB 이하만 지원합니다.");
        var file = JsonSerializer.Deserialize<DictionaryFile>(File.ReadAllText(path), DataFormat.Json) ?? throw new InvalidDataException("사전 파일을 읽을 수 없습니다.");
        if (file.SchemaVersion != 1 || file.ProtocolScope != DataFormat.Scope || file.Buffs is null || file.Buffs.Count > 10000) throw new InvalidDataException("지원하지 않는 사전 형식 또는 게임 규격입니다.");
        foreach (var b in file.Buffs) DataFormat.Validate(b);
        if (file.Buffs.Select(b => b.StatusId).Distinct().Count() != file.Buffs.Count) throw new InvalidDataException("중복된 상태 ID가 있습니다.");
        return file;
    }
    public void Import(DictionaryFile file, bool replaceConflicts)
    {
        if (file.SchemaVersion != 1 || file.ProtocolScope != DataFormat.Scope || file.Buffs is null) throw new InvalidDataException("사전 규격이 맞지 않습니다.");
        foreach (var b in file.Buffs) DataFormat.Validate(b);
        if (file.Buffs.Select(b => b.StatusId).Distinct().Count() != file.Buffs.Count) throw new InvalidDataException("중복된 상태 ID입니다.");
        Backup();
        using var tx = db.BeginTransaction();
        foreach (var b in file.Buffs)
        {
            using var cmd = db.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = replaceConflicts ? "INSERT INTO definitions VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json" : "INSERT OR IGNORE INTO definitions VALUES($id,$json)";
            cmd.Parameters.AddWithValue("$id", b.StatusId);
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(b with { Source = "user" }, DataFormat.Json));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
    public string Backup()
    {
        var dir = Path.Combine(Folder, "backups"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"buffs-{DateTime.Now:yyyyMMdd-HHmmss-fffffff}.db");
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        target.Open(); db.BackupDatabase(target);
        foreach (var old in new DirectoryInfo(dir).GetFiles("*.db").OrderByDescending(f => f.Name).Skip(20)) old.Delete();
        return path;
    }
    public void Dispose() => db.Dispose();
}
