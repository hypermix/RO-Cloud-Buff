using System.Text.Json;

namespace RoBuff.Core;

public sealed record BuffDefinition(int StatusId, string Name, string Category = "skill", bool Ignored = false, bool Enabled = true, bool Alert = false, int WarningSeconds = 10, string Source = "user");
public sealed record Discovery(int StatusId, int Count, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, uint? LastTotalMs, uint? LastLeftMs);
public sealed record Profile(string Id, string Label, string ProcessName);
public sealed record CardRule(string ProfileId, int StatusId, bool Visible, bool Alert, int WarningSeconds);
public sealed record StatusEvent(DateTimeOffset Time, ushort Opcode, int StatusId, uint ActorId, bool Active, uint? TotalMs, uint? LeftMs);
public sealed record SkillRequest(DateTimeOffset Time, int SkillId, int Level, uint TargetId);
public sealed record ActorIdentity(DateTimeOffset Time, uint ActorId);
public sealed record DictionaryFile(int SchemaVersion, string ProtocolScope, List<BuffDefinition> Buffs);
public static class DataFormat
{
    public const string Scope = "kRO-main-observed-2026";
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static void Validate(BuffDefinition b)
    {
        if (b.StatusId < 0 || b.StatusId > 10000 || string.IsNullOrWhiteSpace(b.Name) || b.Name.Length > 100)
            throw new InvalidDataException("상태 ID 또는 버프 이름이 올바르지 않습니다.");
        if (b.Category is not ("skill" or "potion" or "other") || b.WarningSeconds < 0 || b.WarningSeconds > 3600)
            throw new InvalidDataException("종류 또는 알림 시간이 올바르지 않습니다.");
    }
}

public sealed class BuffState
{
    public required int StatusId { get; init; }
    public bool Active { get; set; }
    public DateTimeOffset? Expires { get; set; }
    public DateTimeOffset? PendingEnd { get; set; }
    public bool EndAnnounced { get; set; }
    public bool WarningAnnounced { get; set; }
    public bool ExpiredAnnounced { get; set; }
}

public sealed class BuffTracker
{
    public Dictionary<int, BuffState> States { get; } = [];
    public void Apply(StatusEvent e)
    {
        if (!States.TryGetValue(e.StatusId, out var state)) States[e.StatusId] = state = new() { StatusId = e.StatusId };
        if (e.Active)
        {
            state.Active = true;
            state.Expires = e.LeftMs is uint ms && ms != uint.MaxValue ? e.Time.AddMilliseconds(ms) : null;
            state.PendingEnd = null;
            state.EndAnnounced = state.WarningAnnounced = state.ExpiredAnnounced = false;
        }
        else state.PendingEnd = e.Time;
    }
    // A short grace interval absorbs the remove/add pair sent when a buff is refreshed.
    public List<(int Status, string Kind)> Advance(DateTimeOffset now)
    {
        List<(int, string)> result = [];
        foreach (var s in States.Values)
        {
            if (s.PendingEnd is { } end && now - end >= TimeSpan.FromMilliseconds(500))
            {
                s.Active = false;
                if (!s.EndAnnounced) { result.Add((s.StatusId, "종료")); s.EndAnnounced = true; }
            }
        }
        return result;
    }
    public void Reset() => States.Clear();
}
