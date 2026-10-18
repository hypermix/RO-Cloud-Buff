using System.Media;
namespace RoBuff.App;
public sealed partial class CloudWindow
{
    SoundPlayer? alertPlayer;
    MemoryStream? alertStream;
    static byte[] ChimeWave(int volume){
        const int rate=22050;int count=(int)(rate*.38);double gain=Math.Clamp(volume,0,100)/100.0*.24;
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(count*2);
        for(int i=0;i<count;i++){double t=(double)i/rate;double note=t<.16?t:t-.17;double envelope=note<0?0:Math.Min(1,note/.012)*Math.Exp(-note*16);double sample=Math.Sin(2*Math.PI*(t<.16?660:880)*Math.Max(0,note))*envelope*gain;writer.Write((short)(sample*short.MaxValue));}
        return stream.ToArray();
    }
    void PlayAlert(int volume){if(volume<=0)return;alertPlayer?.Stop();alertPlayer?.Dispose();alertStream?.Dispose();alertStream=new MemoryStream(ChimeWave(volume));alertPlayer=new SoundPlayer(alertStream);alertPlayer.Play();}
}
