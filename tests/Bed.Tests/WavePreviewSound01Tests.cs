using System.Reflection;
using System.Text;
using Bed.Editor.App;
using Xunit;
namespace Bed.Tests;
public sealed class WavePreviewSound01Tests
{
    private static readonly MethodInfo Decode=typeof(EditorApp).Assembly.GetType("Bed.Editor.App.WavePreviewDecoder")!.GetMethod("Decode",BindingFlags.Static|BindingFlags.NonPublic)!;
    // A typed delegate keeps ReadOnlySpan out of reflection argument boxing.
    private delegate object Decoder(ReadOnlySpan<byte> bytes);
    private static object Read(byte[] bytes)=>Decode.CreateDelegate<Decoder>()(bytes);
    private static byte[] Wave(ushort tag,ushort bits,ushort channels,byte[] data)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream,Encoding.UTF8,true);
        writer.Write("RIFF"u8);writer.Write((uint)(36+data.Length));writer.Write("WAVEfmt "u8);writer.Write(16u);writer.Write(tag);writer.Write(channels);writer.Write(8000u);writer.Write((uint)(8000*channels*(bits/8)));writer.Write((ushort)(channels*(bits/8)));writer.Write(bits);writer.Write("data"u8);writer.Write((uint)data.Length);writer.Write(data);return stream.ToArray();
    }
    [Theory][InlineData(8)][InlineData(16)]
    public void PCM_metadata_and_owned_samples_are_exact(int bits)
    {
        var bytes=Wave(1,(ushort)bits,1,bits==8?[0,128,255,128]:[0,128,0,0,255,127,0,0]);var result=Read(bytes);var type=result.GetType();
        Assert.Equal(8000,type.GetProperty("SampleRate")!.GetValue(result));Assert.Equal(1,type.GetProperty("Channels")!.GetValue(result));
        Assert.Equal(bits==8?new short[]{-32768,0,32512,0}:new short[]{-32768,0,32767,0},(short[])type.GetProperty("Samples")!.GetValue(result)!);
        Assert.Equal(4L,type.GetProperty("Frames",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(result));
        Assert.Equal(0.0005,type.GetProperty("Seconds",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(result));
        Assert.Equal($"WAV PCM{bits}",type.GetProperty("Format")!.GetValue(result));Array.Clear(bytes);Assert.Equal(-32768,((short[])type.GetProperty("Samples")!.GetValue(result)!)[0]);
    }
    [Fact]
    public void Stereo_float_clamps_peaks_and_counts_frames_instead_of_samples()
    {
        var samples=new[]{-2f,2f,-0.5f,0.5f};
        var bytes=Wave(3,32,2,samples.SelectMany(BitConverter.GetBytes).ToArray());
        var result=Read(bytes);var type=result.GetType();
        Assert.Equal(new short[]{-32768,32767,-16384,16384},(short[])type.GetProperty("Samples")!.GetValue(result)!);
        Assert.Equal(2,type.GetProperty("Channels")!.GetValue(result));
        Assert.Equal(2L,type.GetProperty("Frames",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(result));
        Assert.Equal("WAV float32",type.GetProperty("Format")!.GetValue(result));
    }
    [Fact]
    public void Unknown_odd_sized_chunk_is_skipped_with_padding_and_overflow_is_refused()
    {
        var original=Wave(1,16,1,[0,0]);
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream,Encoding.UTF8,true);
        writer.Write(original.AsSpan(0,12));writer.Write("JUNK"u8);writer.Write(1u);writer.Write((byte)42);writer.Write((byte)0);writer.Write(original.AsSpan(12));
        var bytes=stream.ToArray();System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4),(uint)(bytes.Length-8));
        var result=Read(bytes);Assert.Equal(new short[]{0},(short[])result.GetType().GetProperty("Samples")!.GetValue(result)!);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16),uint.MaxValue);
        Assert.Contains("beyond the file",Assert.Throws<InvalidDataException>(()=>Read(bytes)).Message);
    }
    [Theory][InlineData("truncated")][InlineData("length")][InlineData("format")][InlineData("alignment")][InlineData("nan")]
    public void Damaged_or_unsupported_audio_has_a_plain_refusal(string mode)
    {
        var bytes=Wave(mode=="nan"?(ushort)3:(ushort)1,mode=="nan"?(ushort)32:(ushort)16,1,mode=="nan"?BitConverter.GetBytes(float.NaN):new byte[]{0,0});
        switch(mode){case "truncated":bytes=bytes[..10];break;case "length":bytes[4]=0;break;case "format":bytes[20]=99;break;case "alignment":bytes[32]=7;break;}
        Assert.StartsWith("WAV preview unavailable:",Assert.Throws<InvalidDataException>(()=>Read(bytes)).Message);
    }
}
