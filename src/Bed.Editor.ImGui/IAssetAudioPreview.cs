namespace Bed.Editor.ImGui;

/// <summary>Decoded sound metadata; frames count simultaneous channel samples.</summary>
/// <param name="Format">Decoder/encoding description.</param>
/// <param name="Channels">Number of channels.</param>
/// <param name="SampleRate">Frames per second.</param>
/// <param name="Frames">Decoded frame count.</param>
public sealed record AudioPreviewInfo(string Format, int Channels, int SampleRate, long Frames)
{
    /// <summary>Decoded duration in seconds.</summary>
    public double Seconds => SampleRate > 0 ? (double)Frames / SampleRate : 0;
}

/// <summary>Owner-thread preview over admitted immutable bytes; never reads browser paths.</summary>
public interface IAssetAudioPreview : IDisposable
{
    /// <summary>Decodes and caches one admitted sound; null with Status on refusal. Does not play.</summary>
    AudioPreviewInfo? Inspect(AssetCatalog.Lease content);
    /// <summary>Plays this exact admitted content, replacing any previous preview.</summary>
    bool Play(AssetCatalog.Lease content);
    /// <summary>Stops playback; safe before first play and after disposal.</summary>
    void Stop();
    /// <summary>True while the backend is playing.</summary>
    bool IsPlaying { get; }
    /// <summary>Plain decoder/device feedback, without a claim of distribution rights.</summary>
    string Status { get; }
}
