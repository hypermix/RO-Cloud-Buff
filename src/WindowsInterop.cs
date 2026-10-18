using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace RoBuff.App;

public sealed record GameWindow(nint Handle, int Pid, long StartedTicks, string Title, string ProcessName);
public sealed record Connection(IPAddress Local, int LocalPort, IPAddress Remote, int RemotePort, int Pid)
{
    public string Key => $"{Local}:{LocalPort}>{Remote}:{RemotePort}";
    public bool Loopback => IPAddress.IsLoopback(Local) && IPAddress.IsLoopback(Remote);
    public bool Outgoing(RoBuff.Core.TcpFrame f) => f.Source.Equals(Local) && f.SourcePort == LocalPort && f.Destination.Equals(Remote) && f.DestinationPort == RemotePort;
    public bool Incoming(RoBuff.Core.TcpFrame f) => f.Source.Equals(Remote) && f.SourcePort == RemotePort && f.Destination.Equals(Local) && f.DestinationPort == LocalPort;
    public string Filter => $"tcp and ((src host {Local} and src port {LocalPort} and dst host {Remote} and dst port {RemotePort}) or (src host {Remote} and src port {RemotePort} and dst host {Local} and dst port {LocalPort}))";
}
public static class WindowsInterop
{
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, System.Text.StringBuilder text, int max);
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(nint table, ref int size, bool order, int family, int type, uint reserved);
    public static GameWindow Inspect(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        using var p = Process.GetProcessById((int)pid);
        var title = new System.Text.StringBuilder(512); GetWindowText(hwnd, title, title.Capacity);
        if (!p.ProcessName.Contains("ragexe", StringComparison.OrdinalIgnoreCase) && !p.ProcessName.Contains("ragnarok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("라그나로크 게임 창을 선택해주세요. 선택한 프로세스: " + p.ProcessName);
        return new(hwnd, p.Id, p.StartTime.ToUniversalTime().Ticks, title.ToString(), p.ProcessName);
    }
    public static bool Alive(GameWindow window)
    {
        try { return IsWindow(window.Handle) && Inspect(window.Handle) is var current && current.Pid == window.Pid && current.StartedTicks == window.StartedTicks; }
        catch { return false; }
    }
    public static List<Connection> Connections(int pid)
    {
        List<Connection> result = [];
        foreach (int family in new[] { 2, 23 })
        {
            int size = 0; uint code = GetExtendedTcpTable(0, ref size, false, family, 5, 0);
            if (code != 122 && code != 0) throw new Win32Exception((int)code);
            if (size < 4) continue;
            nint buffer = Marshal.AllocHGlobal(size);
            try
            {
                // Endpoint counts can grow between calls. Retry with the required size next poll.
                code = GetExtendedTcpTable(buffer, ref size, false, family, 5, 0);
                if (code == 122) continue;
                if (code != 0) throw new Win32Exception((int)code);
                int count = Marshal.ReadInt32(buffer), rowSize = family == 2 ? 24 : 56;
                if (count < 0 || 4L + (long)count*rowSize > size) throw new InvalidDataException("TCP 연결 목록 크기가 올바르지 않습니다.");
                for (int i = 0; i < count; i++)
                {
                    nint r = buffer + 4 + i*rowSize;
                    int owner = Marshal.ReadInt32(r, family == 2 ? 20 : 52);
                    int state = Marshal.ReadInt32(r, family == 2 ? 0 : 48);
                    if (owner != pid || state != 5) continue;
                    IPAddress local, remote;
                    int lp, rp;
                    if (family == 2)
                    {
                        local = new(BitConverter.GetBytes(Marshal.ReadInt32(r, 4)));
                        remote = new(BitConverter.GetBytes(Marshal.ReadInt32(r, 12)));
                        lp = Port(r+8); rp = Port(r+16);
                    }
                    else
                    {
                        byte[] a = new byte[16], b = new byte[16]; Marshal.Copy(r,a,0,16); Marshal.Copy(r+24,b,0,16);
                        local = new(a, (uint)Marshal.ReadInt32(r,16)); remote = new(b, (uint)Marshal.ReadInt32(r,40));
                        lp = Port(r+20); rp = Port(r+44);
                    }
                    result.Add(new(local,lp,remote,rp,pid));
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return result;
    }
    private static int Port(nint p) => (Marshal.ReadByte(p)<<8) | Marshal.ReadByte(p,1);
}
