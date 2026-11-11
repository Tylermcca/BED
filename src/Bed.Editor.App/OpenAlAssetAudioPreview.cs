using System.Runtime.InteropServices;
using Bed.Editor.ImGui;
using Brutal.OpenAlApi;
using NVorbis;

namespace Bed.Editor.App;

// MIXED: BRUTAL OpenAL lifecycle recipe and managed NVorbis; preview ownership/limits are local.
internal sealed class OpenAlAssetAudioPreview : IAssetAudioPreview
{
    private AssetCatalog.Lease? cached;
    private DecodedPreviewSound? decoded;
    private Device? device;
    private Context? context;
    private AlSource source;
    private AlBuffer buffer;
    private bool hasSource, hasBuffer, disposed;
    public string Status { get; private set; } = "Select a sound to preview.";
    public bool IsPlaying
    {
        get
        {
            if (disposed || !hasSource || context is null) return false;
            context.MakeCurrent();
            var playing = source.State == AlSourceState.Playing;
            if (!playing && Status == "Playing sound.") Status = "Sound finished.";
            return playing;
        }
    }
    public AudioPreviewInfo? Inspect(AssetCatalog.Lease content)
    {
        if (disposed) { Status = "Sound preview is closed."; return null; }
        if (!ReferenceEquals(cached, content))
        {
            Stop(); cached = content; decoded = null;
            try
            {
                if (content.Length > WavePreviewDecoder.MaxEncodedBytes) throw new InvalidDataException("Sound exceeds the 32 MiB encoded preview limit.");
                var bytes = new byte[content.Length]; content.CopyTo(bytes);
                decoded = Decode(bytes);
                Status = "Sound ready. Press Play sound or F6.";
            }
            catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or InvalidOperationException or OverflowException or IndexOutOfRangeException or NotSupportedException)
            { Status = "Sound preview unavailable: " + e.Message; }
        }
        return decoded is {} sound ? new(sound.Format, sound.Channels, sound.SampleRate, sound.Frames) : null;
    }
    internal static DecodedPreviewSound Decode(byte[] bytes)
    {
        if (bytes.Length > WavePreviewDecoder.MaxEncodedBytes) throw new InvalidDataException("Sound exceeds the 32 MiB encoded preview limit.");
        if (bytes.AsSpan().StartsWith("RIFF"u8)) return WavePreviewDecoder.Decode(bytes);
        if (!bytes.AsSpan().StartsWith("OggS"u8)) throw new InvalidDataException("Expected WAV or OGG/Vorbis audio.");
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new VorbisReader(stream, false);
        if (reader.Channels is not (1 or 2) || reader.SampleRate is <1 or >192000) throw new InvalidDataException("Use mono/stereo audio from 1 to 192000 Hz.");
        if (reader.TotalSamples < 0 || reader.TotalSamples > WavePreviewDecoder.MaxDecodedBytes / (2L * reader.Channels)) throw new InvalidDataException("Decoded sound exceeds the 64 MiB preview limit.");
        var pcm = new List<short>(); var chunk = new float[8192];
        int count;
        while ((count = reader.ReadSamples(chunk, 0, chunk.Length)) > 0)
        {
            if ((long)pcm.Count + count > WavePreviewDecoder.MaxDecodedBytes / 2) throw new InvalidDataException("Decoded sound exceeds the 64 MiB preview limit.");
            for (var i = 0; i < count; i++)
            {
                var value = chunk[i];
                if (!float.IsFinite(value)) throw new InvalidDataException("A decoded sample is not finite.");
                pcm.Add(value <= -1 ? short.MinValue : value >= 1 ? short.MaxValue : (short)MathF.Round(value * short.MaxValue));
            }
        }
        if (pcm.Count % reader.Channels != 0 || reader.Streams.Count != 1 || reader.FindNextStream()) throw new InvalidDataException("Chained or misaligned OGG audio is unsupported.");
        return new("OGG Vorbis", reader.Channels, reader.SampleRate, pcm.ToArray());
    }
    public bool Play(AssetCatalog.Lease content)
    {
        if (Inspect(content) is null) return false;
        Stop();
        try
        {
            device ??= Alc.OpenDevice();
            context ??= device.CreateContext(); context.MakeCurrent();
            buffer = Al.GenBuffer(); hasBuffer = true;
            buffer.SetData(decoded!.Channels == 1 ? AlFormat.Mono16 : AlFormat.Stereo16, MemoryMarshal.AsBytes(decoded.Samples.AsSpan()), decoded.SampleRate);
            Al.ThrowIfError();
            source = Al.GenSource(); hasSource = true;
            source.Buffer = buffer; source.SourceRelative = true;
            source.Play(); Al.ThrowIfError(); Status = "Playing sound."; return true;
        }
        catch (Exception e) when (e is AlException or AlcException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { Stop(); Status = "Sound playback unavailable: " + e.Message; return false; }
    }
    public void Stop()
    {
        if (context is not null)
        {
            context.MakeCurrent();
            if (hasSource) { source.Stop(); source.Delete(); hasSource = false; }
            if (hasBuffer) { buffer.Delete(); hasBuffer = false; }
        }
        if (!disposed) Status = "Sound stopped.";
    }
    public void Dispose()
    {
        if (disposed) return;
        try { Stop(); }
        finally
        {
            disposed = true; cached = null; decoded = null;
            if (context is not null) { Context.MakeCurrent(null); context.Dispose(); context = null; }
            device?.Dispose(); device = null;
        }
    }
}
