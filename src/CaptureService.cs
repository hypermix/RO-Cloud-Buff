using System.Net.NetworkInformation;
using RoBuff.Core;
using SharpPcap;

namespace RoBuff.App;

public sealed record ObservedActor(uint ActorId,int StatusId,uint? LeftMs,DateTimeOffset At);
public sealed record CaptureDiagnostics(int Connections,int Flows,long Packets,long IncomingBytes,long OutgoingBytes,long Statuses,long Requests,List<ObservedActor> Candidates,bool Calibrating=false,long? CalibrationUntil=null);

public sealed class LiveSession : IDisposable
{
    private readonly GameWindow window;
    private readonly Action<string> status;
    private readonly Action<uint> candidate;
    private readonly Action<StatusEvent> receive;
    private readonly CancellationTokenSource cancel = new();
    private readonly Dictionary<string, Flow> flows = [];
    private readonly object sync = new();
    private readonly Task work;
    private uint? actor;
    private readonly Action<CaptureDiagnostics>? diagnostics;
    private long packets,incomingBytes,outgoingBytes,statuses,requests;
    private readonly IdentityCalibration calibration=new();
    public void StartCalibration(bool narrow){lock(sync){if(actor is not null)throw new InvalidOperationException("이미 본인 확인이 완료되었습니다.");calibration.Start(DateTimeOffset.UtcNow,narrow);}}
    private readonly Dictionary<uint,ObservedActor> observed=[];
    public bool HasObservedActor(uint id){lock(sync)return calibration.CanConfirm(id,DateTimeOffset.UtcNow);}
    public LiveSession(GameWindow window, Action<string> status, Action<uint> candidate, Action<StatusEvent> receive,Action<CaptureDiagnostics>? diagnostics=null)
    {
        this.window=window; this.status=status; this.candidate=candidate; this.receive=receive;
        this.diagnostics=diagnostics;
        work=Task.Run(Run);
    }
    public void Confirm(uint id)
    {
        lock(sync)
        {
            if(!calibration.CanConfirm(id,DateTimeOffset.UtcNow))throw new InvalidOperationException("확인 후보가 만료되었습니다. 본인 확인을 다시 시작해주세요.");
            actor=id;calibration.Reset();
            foreach(var flow in flows.Values)
            {
                foreach(var e in flow.Recent.Where(e=>e.ActorId==id)) receive(e);
                flow.Recent.Clear();
            }
        }
    }
    private async Task Run()
    {
        try
        {
            while(!cancel.IsCancellationRequested)
            {
                if(!WindowsInterop.Alive(window)) { status("게임 종료 · 창 다시 지정"); break; }
                try
                {
                    var connections=WindowsInterop.Connections(window.Pid);
                    List<Flow> removed=[];
                    lock(sync)
                    {
                        foreach(var stale in flows.Keys.Except(connections.Select(c=>c.Key)).ToArray())
                        { removed.Add(flows[stale]); flows.Remove(stale); }
                        if(removed.Count>0)
                        {
                            observed.Clear();
                            foreach(var flow in flows.Values) {flow.Recent.Clear();flow.IdentityHint=null;}
                            status(actor is null?"연결 변경 · 버프 수신 대기":"연결 재탐색 중 · 본인 확인 유지");
                        }
                    }
                    // StopCapture may wait for its callback. Never wait while holding the callback lock.
                    foreach(var f in removed)f.Dispose();
                    lock(sync)
                    {
                        foreach(var c in connections.Where(c=>!flows.ContainsKey(c.Key)))
                        {
                            var device=FindDevice(c);
                            if(device is null) continue;
                            Flow? flow=null;
                            try
                            {
                                device.Open(mode:DeviceModes.None, read_timeout:100);
                                device.Filter=c.Filter;
                                flow=new Flow(c,device);
                                device.OnPacketArrival += (_,packet)=>
                                {
                                    try { OnPacket(flow,packet.GetPacket()); }
                                    catch(Exception ex) { status("해석 중단: "+ex.Message); }
                                };
                                flows.Add(c.Key,flow);
                                device.StartCapture();
                            }
                            catch { if(flow!=null) flows.Remove(c.Key); device.Dispose(); throw; }
                        }
                        status(flows.Count==0 ? (actor is null?"연결 대기 · 맵에 접속해주세요":"연결 재탐색 중 · 본인 확인 유지") : actor is null ? "연결됨 · 본인 확인 시작을 눌러주세요" : "연결됨 · 수신 상태 추적 중");
                        diagnostics?.Invoke(new(connections.Count,flows.Count,packets,incomingBytes,outgoingBytes,statuses,requests,calibration.Candidates(DateTimeOffset.UtcNow).OrderByDescending(a=>a.Time).Take(128).Select(a=>new ObservedActor(a.ActorId,a.StatusId,a.LeftMs,a.Time)).ToList(),calibration.Until is { } until&&DateTimeOffset.UtcNow<=until,calibration.Until?.ToUnixTimeMilliseconds()));
                    }
                }
                catch(Exception ex) { status("연결 확인 필요: "+ex.Message); }
                await Task.Delay(1500,cancel.Token);
            }
        }
        catch(OperationCanceledException) { }
        finally
        {
            Flow[] close;
            lock(sync) { close=flows.Values.ToArray(); flows.Clear(); }
            foreach(var f in close) f.Dispose();
        }
    }
    private ICaptureDevice? FindDevice(Connection c)
    {
        var devices=CaptureDeviceList.New();
        if(c.Loopback) return devices.FirstOrDefault(d=>d.Name.Contains("Loopback",StringComparison.OrdinalIgnoreCase));
        var ids=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.Equals(c.Local))).Select(n=>n.Id.Trim('{','}')).ToArray();
        return devices.FirstOrDefault(d=>ids.Any(id=>d.Name.Contains(id,StringComparison.OrdinalIgnoreCase)));
    }
    private void OnPacket(Flow flow,RawCapture raw)
    {
        var frame=Frames.Decode((int)raw.LinkLayerType,raw.Data);
        if(frame is null) return;
        lock(sync)
        {
            if(cancel.IsCancellationRequested || !flows.TryGetValue(flow.Connection.Key,out var current) || current!=flow) return;
            var now=DateTimeOffset.UtcNow;
            packets++;
            if(flow.Connection.Outgoing(frame))
            {
                outgoingBytes+=frame.Payload.Length;
                var bytes=flow.Outgoing.Push(frame.Sequence,frame.Payload);
                foreach(var request in flow.Requests.Push(bytes,now))
                {
                    requests++;
                    if(request is ActorIdentity identity) flow.IdentityHint=(identity.ActorId,now);
                    // Self-buff calibration is explicit in the UI. Other target skills never choose an actor.
                    if(request is SkillRequest skill && skill.SkillId is 29 or 34 or 2041 or 2042)
                        flow.IdentityHint=(skill.TargetId,now);
                }
            }
            else if(flow.Connection.Incoming(frame))
            {
                incomingBytes+=frame.Payload.Length;
                var bytes=flow.Incoming.Push(frame.Sequence,frame.Payload);
                foreach(var e in flow.Decoder.Push(bytes,now))
                {
                    statuses++;calibration.Observe(e);
                    if(e.Active&&e.StatusId is 10 or 12){observed[e.ActorId]=new(e.ActorId,e.StatusId,e.LeftMs,now);foreach(var stale in observed.Where(p=>now-p.Value.At>TimeSpan.FromSeconds(20)).Select(p=>p.Key).ToArray())observed.Remove(stale);}
                    if(actor is uint id) { if(e.ActorId==id) receive(e); }
                    else
                    {
                        if(flow.Recent.Count>=1000) flow.Recent.RemoveAt(0);
                        flow.Recent.Add(e);
                        // Candidates come only from an explicit incoming observation round.
                    }
                }
                if(flow.Incoming.Stalled || flow.Outgoing.Stalled) status("통신 누락 감지 · 창을 다시 지정해주세요");
            }
        }
    }
    public void Dispose() { cancel.Cancel(); }
    public Task Completion => work;
    private sealed class Flow(Connection connection, ICaptureDevice device) : IDisposable
    {
        public Connection Connection { get; }=connection;
        public ICaptureDevice Device { get; }=device;
        public TcpAssembler Incoming { get; }=new();
        public TcpAssembler Outgoing { get; }=new();
        public StatusDecoder Decoder { get; }=new();
        public RequestDecoder Requests { get; }=new();
        public List<StatusEvent> Recent { get; }=[];
        public (uint Id,DateTimeOffset At)? IdentityHint { get; set; }
        public void Dispose() { try { Device.StopCapture(); } catch { } Device.Dispose(); }
    }
}
