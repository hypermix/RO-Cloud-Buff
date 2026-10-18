using System.Buffers.Binary;
using System.Net;

namespace RoBuff.Core;

public sealed record ReplayEvent(string ConnectionKey, StatusEvent Event);
public static class Replay
{
    public static List<ReplayEvent> Read(string path)
    {
        using var input=File.OpenRead(path); using var reader=new BinaryReader(input);
        uint magic=reader.ReadUInt32();
        if(magic!=0xa1b2c3d4) throw new InvalidDataException("리플레이는 little-endian PCAP(마이크로초)만 지원합니다.");
        var header=reader.ReadBytes(20);
        if(header.Length!=20) throw new InvalidDataException("PCAP 헤더가 잘렸습니다.");
        int link=(int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16));
        Dictionary<string,(TcpAssembler Tcp,StatusDecoder Decoder)> streams=[];
        List<ReplayEvent> events=[];
        while(input.Position<input.Length)
        {
            var rh=reader.ReadBytes(16);
            if(rh.Length!=16) throw new InvalidDataException("PCAP 레코드가 잘렸습니다.");
            uint sec=BinaryPrimitives.ReadUInt32LittleEndian(rh), micro=BinaryPrimitives.ReadUInt32LittleEndian(rh.AsSpan(4)), count=BinaryPrimitives.ReadUInt32LittleEndian(rh.AsSpan(8));
            if(count>1024*1024 || micro>=1000000) throw new InvalidDataException("잘못된 PCAP 레코드입니다.");
            var packet=reader.ReadBytes((int)count);
            if(packet.Length!=count) throw new InvalidDataException("PCAP 데이터가 잘렸습니다.");
            var frame=Frames.Decode(link,packet);
            if(frame is null || frame.Payload.Length==0) continue;
            if(!streams.TryGetValue(frame.DirectionKey,out var stream)) streams[frame.DirectionKey]=stream=(new(),new());
            var bytes=stream.Tcp.Push(frame.Sequence,frame.Payload);
            var time=DateTimeOffset.FromUnixTimeSeconds(sec).AddTicks(micro*10);
            foreach(var e in stream.Decoder.Push(bytes,time)) events.Add(new(frame.DirectionKey,e));
        }
        return events.OrderBy(e=>e.Event.Time).ToList();
    }
}
