using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Data.Sqlite;
using RoBuff.Core;
namespace RoBuff.App;

public static class Program
{
    [STAThread] public static void Main(){var app=new Application();app.DispatcherUnhandledException+=(_,e)=>{var args=Environment.GetCommandLineArgs();if(args.Length>2&&args[1]=="--smoke-test"){File.WriteAllText(args[2]+".error",e.Exception.ToString());e.Handled=true;app.Shutdown(1);return;}MessageBox.Show(e.Exception.Message,"RO CLOUD BUFF");e.Handled=true;};app.Run(new CloudWindow());}
}
public sealed partial class CloudWindow:Window
{
    readonly WebView2 web=new();
    readonly Dictionary<string,Binding> bindings=[];
    readonly Dictionary<string,GameWindow> windows=[];
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(200)};
    readonly string folder=Environment.GetEnvironmentVariable("RO_CLOUD_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ROCloudBuff");
    SqliteConnection? db;JsonObject? saved;bool closed;
    sealed class Binding{public required string Card;public LiveSession? Session;public uint? Actor,Candidate;public BuffTracker Tracker=new();public string Status="";}
    void ImportLegacySettings()
    {
        // Leave the old database intact; never replace settings already in the new location.
        if(Environment.GetEnvironmentVariable("RO_CLOUD_DATA_DIR") is not null)return;
        var destination=Path.Combine(folder,"settings.db");
        var legacy=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RO CLOUD BUFF","settings.db");
        if(File.Exists(destination)||!File.Exists(legacy))return;
        var temporary=Path.Combine(folder,"settings-migration.db");
        try
        {
            using(var source=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=legacy,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
            using(var target=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=temporary,Pooling=false}.ToString()))
            {
                source.Open();target.Open();source.BackupDatabase(target);
            }
            File.Move(temporary,destination,false);
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    public CloudWindow(){Title="RO CLOUD BUFF";Width=Math.Min(460,SystemParameters.WorkArea.Width);Height=Math.Min(820,SystemParameters.WorkArea.Height);WindowStartupLocation=WindowStartupLocation.CenterScreen;MinWidth=240;MinHeight=240;Background=new SolidColorBrush(Color.FromRgb(32,33,38));ConfigureChrome();Loaded+=async(_,_)=>await Initialize();Closed+=(_,_)=>{closed=true;StopCrosshair();timer.Stop();foreach(var b in bindings.Values)b.Session?.Dispose();if(saved is not null){try{Save(saved);}catch{}}web.Dispose();db?.Dispose();};timer.Tick+=(_,_)=>{foreach(var b in bindings.Values)foreach(var end in b.Tracker.Advance(DateTimeOffset.UtcNow))Push("buff",new{cardId=b.Card,statusId=end.Status,active=false,time=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()});};}
    async Task Initialize(){try{
        LoadingStatus("저장된 설정을 불러오는 중…");
        Directory.CreateDirectory(folder);ImportLegacySettings();db=new(new SqliteConnectionStringBuilder{DataSource=Path.Combine(folder,"settings.db")}.ToString());db.Open();using(var cmd=db.CreateCommand()){cmd.CommandText="CREATE TABLE IF NOT EXISTS state(id INTEGER PRIMARY KEY,json TEXT NOT NULL);CREATE TABLE IF NOT EXISTS backups(id INTEGER PRIMARY KEY AUTOINCREMENT,json TEXT NOT NULL);SELECT json FROM state WHERE id=1";var value=cmd.ExecuteScalar();if(value is string text)saved=JsonNode.Parse(text)?.AsObject();}
        magnetic=saved?["magnetic"]?.GetValue<bool>()??false;UpdateMagnetic();
        LoadingStatus("화면을 준비하는 중…");
        var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(folder,"WebView2"),new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));await web.EnsureCoreWebView2Async(env);
        web.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;web.CoreWebView2.Settings.AreDevToolsEnabled=false;web.CoreWebView2.Settings.IsStatusBarEnabled=false;
        web.CoreWebView2.SetVirtualHostNameToFolderMapping("rocloud.local",Path.Combine(AppContext.BaseDirectory,"Web"),CoreWebView2HostResourceAccessKind.DenyCors);
        web.CoreWebView2.NavigationStarting+=(_,e)=>{if(!e.Uri.StartsWith("https://rocloud.local/",StringComparison.Ordinal))e.Cancel=true;};web.CoreWebView2.NewWindowRequested+=(_,e)=>e.Handled=true;
        web.CoreWebView2.WebMessageReceived+=Message;
        web.CoreWebView2.NavigationCompleted+=async(_,e)=>{if(!e.IsSuccess){LoadingStatus("화면을 불러오지 못했습니다. 프로그램을 다시 실행해주세요.");return;}var args=Environment.GetCommandLineArgs();if(args.Length>2&&args[1]=="--smoke-test"){try{await Task.Delay(800);CheckVectorAndMagnet();if(Icon is null||WindowStyle!=WindowStyle.None)throw new InvalidOperationException("Custom chrome/icon missing");SystemCommands.MaximizeWindow(this);await Task.Delay(100);if(WindowState!=WindowState.Maximized)throw new InvalidOperationException("Maximize failed");SystemCommands.RestoreWindow(this);await Task.Delay(200);File.WriteAllText(args[2]+".host.json",JsonSerializer.Serialize(new{file=Path.GetFileName(Environment.ProcessPath),title=Title,windowStyle=WindowStyle.ToString(),hasIcon=Icon is not null,vectorIcon=true,magneticChecks=true,maximizeRestore=true}));var result=await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({title:document.title,cards:[...document.querySelectorAll('.rb-character-name')].map(e=>e.textContent),native:!!window.roNative,fixture:document.body.textContent.includes('예시 데이터'),errors:window.nativeErrors||[]})");File.WriteAllText(args[2]+".json",result);using var stream=File.Create(args[2]+".png");await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);}catch(Exception ex){File.WriteAllText(args[2]+".error",ex.ToString());}finally{Close();}}};
        web.Source=new Uri("https://rocloud.local/index.html");timer.Start();
    }catch(Exception ex){var launchArgs=Environment.GetCommandLineArgs();if(launchArgs.Length>2&&launchArgs[1]=="--smoke-test"){File.WriteAllText(launchArgs[2]+".error",ex.ToString());Close();return;}Content=new TextBox{Text="RO CLOUD BUFF 시작 실패\n"+ex.Message+"\nMicrosoft Edge WebView2 Runtime과 Npcap 설치 상태를 확인해주세요.",TextWrapping=TextWrapping.Wrap,IsReadOnly=true,Margin=new Thickness(20)};}}
    async void Message(object? sender,CoreWebView2WebMessageReceivedEventArgs e){int id=0;try{if(!e.Source.StartsWith("https://rocloud.local/",StringComparison.Ordinal))return;var data=JsonNode.Parse(e.WebMessageAsJson)!.AsObject();id=data["id"]!.GetValue<int>();var args=data["args"]?.AsObject()??new JsonObject();object? result=null;switch(data["method"]?.GetValue<string>()){
        case "sound":PlayAlert(args["volume"]?.GetValue<int>()??0);break;
        case "uiReady":ShowApp();break;
        case "load":result=new{state=saved,version="0.1.0-alpha.27",dataFolder=folder};break;
        case "save":Save(args["state"]!.AsObject());break;
        case "windows":result=ListWindows();break;
        case "pointWindow":GetCursorPos(out var point);var handle=GetAncestor(WindowFromPoint(point),2);result=Remember(WindowsInterop.Inspect(handle));break;
        case "bind":await Bind(args["cardId"]!.GetValue<string>(),args["token"]!.GetValue<string>());break;
        case "calibrate":{var key=args["cardId"]!.GetValue<string>();if(!bindings.TryGetValue(key,out var b)||b.Session is null)throw new InvalidOperationException("게임 창을 먼저 연결해주세요.");b.Candidate=null;b.Session.StartCalibration(args["narrow"]?.GetValue<bool>()??false);break;}
        case "confirm":{var key=args["cardId"]!.GetValue<string>();if(!bindings.TryGetValue(key,out var binding))throw new InvalidOperationException("게임 창을 먼저 연결해주세요.");var selected=args["actorId"]?.GetValue<uint>()??binding.Candidate;if(selected is not uint actor||!binding.Session!.HasObservedActor(actor))throw new InvalidOperationException("후보가 만료되었습니다. 본인 확인을 다시 시작하고 자신에게 버프를 적용해주세요.");binding.Session!.Confirm(actor);binding.Actor=actor;Push("status",new{cardId=key,status="연결됨 · 버프 추적 중",connected=true,actorId=actor});break;}
        case "pin":Topmost=args["enabled"]!.GetValue<bool>();UpdatePin();break;
        case "unbind":Remove(args["cardId"]!.GetValue<string>());break;
        case "crosshairStart":StartCrosshair();break;
        case "crosshairStop":StopCrosshair();break;
        default:throw new InvalidOperationException("지원하지 않는 요청입니다.");
    }web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{id,result}));}catch(Exception ex){if(!closed)web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new{id,error=ex.Message}));}}
    void Save(JsonObject incoming){if(incoming.ToJsonString().Length>2_000_000)throw new InvalidDataException("설정이 너무 큽니다.");var snapshot=(JsonObject)incoming.DeepClone();snapshot["playing"]=false;snapshot["modal"]=null;snapshot["menu"]=null;snapshot["notice"]="";Topmost=incoming["pin"]?.GetValue<bool>()??false;UpdatePin();snapshot["pin"]=Topmost;snapshot["magnetic"]=magnetic;snapshot["nativeWindow"]=new JsonObject{["width"]=ActualWidth,["height"]=ActualHeight};foreach(var c in snapshot["cards"]?.AsArray()??[]){if(c is not JsonObject card)continue;card["connected"]=false;card["candidate"]=null;card.Remove("captureStats");card.Remove("calibrationStarted");card.Remove("observedCandidates");card.Remove("selectedActor");card["actorId"]=null;card["connectionText"]="게임 창 연결 대기";foreach(var node in card["buffs"]?.AsArray()??[]){if(node is JsonObject b){b["left"]=0;b["presence"]="unknown";b.Remove("expiresAt");b.Remove("warned");}}}
        foreach(var entry in snapshot["discoveries"]?.AsArray()??[]){if(entry is JsonObject item)item["count"]=0;}
        foreach(var entry in snapshot["ignoredEntries"]?.AsArray()??[]){if(entry is JsonObject item)item["count"]=0;}
        using var transaction=db!.BeginTransaction();using var cmd=db.CreateCommand();cmd.Transaction=transaction;cmd.CommandText="INSERT INTO backups(json) SELECT json FROM state WHERE id=1;INSERT INTO state VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json;DELETE FROM backups WHERE id NOT IN (SELECT id FROM backups ORDER BY id DESC LIMIT 10)";cmd.Parameters.AddWithValue("$json",snapshot.ToJsonString());cmd.ExecuteNonQuery();transaction.Commit();saved=snapshot;
        var ids=incoming["cards"]!.AsArray().Select(c=>c!["id"]!.GetValue<string>()).ToHashSet();foreach(var id in bindings.Keys.Where(id=>!ids.Contains(id)).ToArray())Remove(id);
    }
    void Remove(string id){if(bindings.Remove(id,out var b)){b.Session?.Dispose();b.Tracker.Reset();}boundWindows.Remove(id);}
    object Remember(GameWindow window){var token=Guid.NewGuid().ToString("N");windows[token]=window;return new{token,title=window.Title,pid=window.Pid,process=window.ProcessName};}
    object[] ListWindows(){windows.Clear();List<object> found=[];EnumWindows((h,_)=>{if(!IsWindowVisible(h))return true;try{found.Add(Remember(WindowsInterop.Inspect(h)));}catch{}return true;},0);return found.ToArray();}
    async Task Bind(string id,string token){if(!windows.TryGetValue(token,out var target)||!WindowsInterop.Alive(target))throw new InvalidOperationException("게임 창이 변경되었습니다. 목록을 새로고침해주세요.");if(bindings.Any(pair=>pair.Key!=id&&pair.Value.Session is not null&&boundWindows.GetValueOrDefault(pair.Key)?.Pid==target.Pid))throw new InvalidOperationException("이미 다른 카드에서 추적 중인 게임 창입니다.");if(bindings.Remove(id,out var previous)){previous.Session?.Dispose();if(previous.Session is not null)await previous.Session.Completion;}
        var binding=new Binding{Card=id};bindings[id]=binding;boundWindows[id]=target;
        binding.Session=new(target,status=>OnUi(()=>{if(!Current(binding))return;if(status.StartsWith("게임 종료")){binding.Actor=null;binding.Candidate=null;binding.Tracker.Reset();}if(binding.Status==status)return;binding.Status=status;Push("status",new{cardId=id,status,connected=binding.Actor is not null,reset=binding.Actor is null});}),actor=>OnUi(()=>{if(!Current(binding)||binding.Actor is not null)return;binding.Candidate=actor;Push("candidate",new{cardId=id,actorId=actor});}),ev=>OnUi(()=>{if(!Current(binding)||binding.Actor!=ev.ActorId)return;binding.Tracker.Apply(ev);if(ev.Active)Push("buff",new{cardId=id,statusId=ev.StatusId,active=true,totalMs=ev.TotalMs,leftMs=ev.LeftMs,time=ev.Time.ToUnixTimeMilliseconds()});}),diagnostic=>OnUi(()=>{if(!Current(binding))return;Push("diagnostics",new{cardId=id,connections=diagnostic.Connections,flows=diagnostic.Flows,packets=diagnostic.Packets,incomingBytes=diagnostic.IncomingBytes,outgoingBytes=diagnostic.OutgoingBytes,statuses=diagnostic.Statuses,requests=diagnostic.Requests,calibrating=diagnostic.Calibrating,calibrationUntil=diagnostic.CalibrationUntil,candidates=binding.Actor is null?diagnostic.Candidates.Select(c=>new{actorId=c.ActorId,statusId=c.StatusId,leftMs=c.LeftMs,at=c.At.ToUnixTimeMilliseconds()}).ToArray():[]});}));
        binding.Session.StartCalibration(false);
    }
    readonly Dictionary<string,GameWindow> boundWindows=[];
    bool Current(Binding b)=>bindings.TryGetValue(b.Card,out var current)&&ReferenceEquals(b,current);
    void OnUi(Action action){if(!closed)Dispatcher.BeginInvoke(()=>{if(!closed)action();});}
    void Push(string type,object data){if(!closed&&web.CoreWebView2 is not null)web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{type,data}));}
    delegate bool EnumProc(nint h,nint param);
    [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,nint param);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(nint h);
    [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
    [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")]static extern nint GetAncestor(nint h,uint flags);
}
