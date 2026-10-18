using System.Buffers.Binary;
using System.Net;

namespace RoBuff.Core;

public sealed record TcpFrame(IPAddress Source, int SourcePort, IPAddress Destination, int DestinationPort, uint Sequence, byte[] Payload, bool FinOrReset)
{
    public string DirectionKey => $"{Source}:{SourcePort}>{Destination}:{DestinationPort}";
}
public static class Frames
{
    public static TcpFrame? Decode(int linkType, ReadOnlySpan<byte> packet)
    {
        var offset = linkType switch { 0 or 108 => 4, 1 => 14, 12 or 101 => 0, _ => -1 };
        if (offset < 0 || packet.Length <= offset) return null;
        if (linkType == 1)
        {
            var ether = BinaryPrimitives.ReadUInt16BigEndian(packet[12..]);
            while (ether is 0x8100 or 0x88a8)
            {
                if (packet.Length < offset + 4) return null;
                ether = BinaryPrimitives.ReadUInt16BigEndian(packet[(offset+2)..]); offset += 4;
            }
            if (ether is not (0x0800 or 0x86dd)) return null;
        }
        var ip = packet[offset..];
        ReadOnlySpan<byte> tcp;
        IPAddress src, dst;
        if (ip[0] >> 4 == 4)
        {
            if (ip.Length < 20 || ip[9] != 6 || (BinaryPrimitives.ReadUInt16BigEndian(ip[6..]) & 0x3fff) != 0) return null;
            int header = (ip[0] & 15)*4, total = BinaryPrimitives.ReadUInt16BigEndian(ip[2..]);
            if (header < 20 || total < header + 20 || total > ip.Length) return null;
            src = new(ip[12..16]); dst = new(ip[16..20]); tcp = ip[header..total];
        }
        else if (ip[0] >> 4 == 6)
        {
            if (ip.Length < 60) return null;
            int total = 40 + BinaryPrimitives.ReadUInt16BigEndian(ip[4..]), start = 40, next = ip[6];
            if (total > ip.Length) return null;
            for (int i = 0; next != 6 && i < 8; i++)
            {
                if (next is not (0 or 43 or 60) || start + 2 > total) return null; // fragments / ESP unsupported
                var previous = start; next = ip[start]; start += (ip[start+1]+1)*8;
                if (start <= previous || start > total) return null;
            }
            if (next != 6 || start+20 > total) return null;
            src = new(ip[8..24]); dst = new(ip[24..40]); tcp = ip[start..total];
        }
        else return null;
        int tcpHeader = (tcp[12]>>4)*4;
        if (tcpHeader < 20 || tcpHeader > tcp.Length) return null;
        uint seq = BinaryPrimitives.ReadUInt32BigEndian(tcp[4..]);
        if ((tcp[13]&2) != 0) seq++;
        return new(src, BinaryPrimitives.ReadUInt16BigEndian(tcp), dst, BinaryPrimitives.ReadUInt16BigEndian(tcp[2..]), seq, tcp[tcpHeader..].ToArray(), (tcp[13]&5) != 0);
    }
}

// Bounded sequence-aware reassembly. Missing segments are held, never joined across a gap.
public sealed class TcpAssembler
{
    private uint? next;
    private readonly List<(uint Seq, byte[] Data)> pending = [];
    public bool Stalled { get; private set; }
    public byte[] Push(uint sequence, byte[] payload)
    {
        if (payload.Length == 0) return [];
        next ??= sequence;
        int delta = unchecked((int)(sequence-next.Value));
        if (delta > 0)
        {
            if (!pending.Any(p => p.Seq == sequence)) pending.Add((sequence, payload));
            if (pending.Sum(p => p.Data.Length) > 1024*1024) { pending.Clear(); Stalled = true; }
            return [];
        }
        List<byte> output = [];
        Append(sequence, payload, output);
        bool advanced;
        do
        {
            advanced = false;
            for (int i = 0; i < pending.Count; i++)
            {
                var item = pending[i];
                if (unchecked((int)(item.Seq-next!.Value)) > 0) continue;
                pending.RemoveAt(i); Append(item.Seq, item.Data, output); advanced = true; break;
            }
        } while (advanced);
        return output.ToArray();
    }
    private void Append(uint seq, byte[] payload, List<byte> output)
    {
        int skip = Math.Max(0, unchecked((int)(next!.Value-seq)));
        if (skip >= payload.Length) return;
        output.AddRange(payload.AsSpan(skip).ToArray()); next += (uint)(payload.Length-skip);
    }
}

// Experimental recognizer for the observed kRO protocol. It scans bounded, reassembled bytes;
// it does not claim to frame every message in the game protocol. Callers must scope by flow/actor.
public sealed class StatusDecoder
{
    private byte[] tail = [];
    public IEnumerable<StatusEvent> Push(ReadOnlySpan<byte> bytes, DateTimeOffset time)
    {
        byte[] data = new byte[tail.Length + bytes.Length];
        tail.CopyTo(data, 0); bytes.CopyTo(data.AsSpan(tail.Length));
        List<StatusEvent> events = [];
        int i = 0;
        while (i+2 <= data.Length)
        {
            int op = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i));
            int length = op switch { 0x0983 => 29, 0x043f => 25, 0x0196 => 9, 0x0984 => 28, 0x08ff => 24, _ => 0 };
            if (length == 0) { i++; continue; }
            if (i+length > data.Length) break;
            var p = data.AsSpan(i, length);
            bool enter = op is 0x0984 or 0x08ff;
            int status = BinaryPrimitives.ReadUInt16LittleEndian(p[(enter ? 6 : 2)..]);
            uint actor = BinaryPrimitives.ReadUInt32LittleEndian(p[(enter ? 2 : 4)..]);
            byte state = enter ? (byte)1 : p[8];
            uint? total = op == 0x0983 ? BinaryPrimitives.ReadUInt32LittleEndian(p[9..]) : op == 0x0984 ? BinaryPrimitives.ReadUInt32LittleEndian(p[8..]) : null;
            uint? left = op == 0x0983 ? BinaryPrimitives.ReadUInt32LittleEndian(p[13..]) : op == 0x0984 ? BinaryPrimitives.ReadUInt32LittleEndian(p[12..]) : op == 0x08ff ? BinaryPrimitives.ReadUInt32LittleEndian(p[8..]) : op == 0x043f ? BinaryPrimitives.ReadUInt32LittleEndian(p[9..]) : null;
            if (status > 10000 || actor == 0 || state > 1 || (op != 0x0196 && state != 1) ||
                (left is uint ms && ms != uint.MaxValue && ms > 7*24*3600*1000U) ||
                (total is uint t && left is uint l && t != uint.MaxValue && l != uint.MaxValue && l > t))
            { i++; continue; }
            events.Add(new(time, (ushort)op, status, actor, state == 1, total, left));
            i += length;
        }
        tail = data.AsSpan(i).ToArray();
        return events;
    }
}

public sealed class RequestDecoder
{
    private byte[] tail = [];
    public List<object> Push(byte[] bytes, DateTimeOffset time)
    {
        byte[] data = [..tail, ..bytes]; List<object> result = []; int i = 0;
        while (i+2 <= data.Length)
        {
            var p = data.AsSpan(i);
            int op = BinaryPrimitives.ReadUInt16LittleEndian(p);
            int size = op switch { 0x0438 => 10, 0x0439 => 8, 0x0436 => 23, _ => 0 };
            if (size == 0) { i++; continue; }
            if (p.Length < size) break;
            if (op == 0x0438)
            {
                int lv = BinaryPrimitives.ReadUInt16LittleEndian(p[2..]);
                int skill = BinaryPrimitives.ReadUInt16LittleEndian(p[4..]);
                uint target = BinaryPrimitives.ReadUInt32LittleEndian(p[6..]);
                if (lv is >0 and <=100 && skill is >0 and <10000 && target != 0) result.Add(new SkillRequest(time, skill, lv, target));
            }
            else
            {
                uint actor = BinaryPrimitives.ReadUInt32LittleEndian(p[(op == 0x0436 ? 2 : 4)..]);
                if (actor != 0) result.Add(new ActorIdentity(time, actor));
            }
            i += size;
        }
        tail = data.AsSpan(i).ToArray();
        return result;
    }
}
