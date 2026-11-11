using System.Buffers.Binary;
namespace Bed.Editor.App;

// BORROWED: RIFF/WAVE PCM and IEEE-float layout. INVENTED: bounded preview conversion/refusals.
internal sealed record DecodedPreviewSound(string Format,int Channels,int SampleRate,short[] Samples)
{
    internal long Frames=>Samples.LongLength/Channels;
    internal double Seconds=>(double)Frames/SampleRate;
}
internal static class WavePreviewDecoder
{
    internal const int MaxEncodedBytes=32*1024*1024;
    internal const int MaxDecodedBytes=64*1024*1024;
    internal static DecodedPreviewSound Decode(ReadOnlySpan<byte> bytes)
    {
        static InvalidDataException Bad(string why)=>new("WAV preview unavailable: "+why);
        if(bytes.Length<12||bytes.Length>MaxEncodedBytes||!bytes[..4].SequenceEqual("RIFF"u8)||!bytes.Slice(8,4).SequenceEqual("WAVE"u8))throw Bad("expected a bounded RIFF/WAVE file.");
        if((long)BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..])+8!=bytes.Length)throw Bad("the RIFF length does not match the file.");
        var formatAt=-1;var formatLength=0;var dataAt=-1;var dataLength=0;
        for(var at=12;at<bytes.Length;)
        {
            if(bytes.Length-at<8)throw Bad("a chunk header is truncated.");
            var length=BinaryPrimitives.ReadUInt32LittleEndian(bytes[(at+4)..]);
            var end=(long)at+8+length;var next=end+(length&1);
            if(next>bytes.Length)throw Bad("a chunk extends beyond the file.");
            if(bytes.Slice(at,4).SequenceEqual("fmt "u8))
            {if(formatAt>=0)throw Bad("more than one format chunk.");formatAt=at+8;formatLength=(int)length;}
            else if(bytes.Slice(at,4).SequenceEqual("data"u8))
            {if(dataAt>=0)throw Bad("more than one audio data chunk.");dataAt=at+8;dataLength=(int)length;}
            at=(int)next;
        }
        if(formatAt<0||formatLength<16||dataAt<0)throw Bad("a complete format and audio data chunk are required.");
        var fmt=bytes.Slice(formatAt,formatLength);var tag=BinaryPrimitives.ReadUInt16LittleEndian(fmt);var channels=BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
        var rate=BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);var byteRate=BinaryPrimitives.ReadUInt32LittleEndian(fmt[8..]);var alignment=BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..]);var bits=BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
        if(channels is not (1 or 2)||rate is 0 or >192000)throw Bad("use mono/stereo audio with a sample rate from 1 to 192000 Hz.");
        if(!(tag==1&&bits is 8 or 16)&&!(tag==3&&bits==32))throw Bad("supported WAV formats are PCM 8/16-bit and IEEE float32.");
        var sampleBytes=bits/8;var block=channels*sampleBytes;
        if(alignment!=block||byteRate!=(long)rate*block||dataLength%block!=0)throw Bad("sample alignment or byte rate is inconsistent.");
        var count=dataLength/sampleBytes;
        if((long)count*2>MaxDecodedBytes)throw Bad("decoded audio exceeds the preview byte limit.");
        var pcm=new short[count];var data=bytes.Slice(dataAt,dataLength);
        for(var i=0;i<count;i++)
        {
            if(bits==8)pcm[i]=(short)((data[i]-128)<<8);
            else if(tag==1)pcm[i]=BinaryPrimitives.ReadInt16LittleEndian(data[(i*2)..]);
            else
            {
                var value=BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data[(i*4)..]));
                if(!float.IsFinite(value))throw Bad("a floating-point sample is not finite.");
                pcm[i]=value<=-1?short.MinValue:value>=1?short.MaxValue:(short)MathF.Round(value*short.MaxValue);
            }
        }
        return new(tag==1?$"WAV PCM{bits}":"WAV float32",channels,(int)rate,pcm);
    }
}
