using System.Security.Cryptography;
using System.Text;
using Brutal.ImGuiApi;
namespace Bed.Editor.App;
using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>App-owned bounded docking settings. Failed or corrupt settings never prevent opening the editor.</summary>
public sealed class EditorLayoutStore(string? path = null)
{
    /// <summary>Maximum encoded settings payload, excluding the small integrity header.</summary>
    public const int MaxBytes = 256 * 1024;
    private const string Prefix = "; BED layout v1 ";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    /// <summary>App-local default; never relative to the launched project or executable.</summary>
    public static string DefaultPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BED", "layout.ini");
    /// <summary>The explicitly selected settings file.</summary>
    public string Path { get; } = path ?? DefaultPath;
    /// <summary>Loads only bounded intact settings written by this store. The checksum detects corruption, not malicious authorship.</summary>
    public bool TryRead(out string ini)
    {
        ini = "";
        try
        {
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length <= Prefix.Length + 65 || stream.Length > MaxBytes + Prefix.Length + 65) return false;
            var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
            var text = Utf8.GetString(bytes); var newline = text.IndexOf('\n');
            if (newline != Prefix.Length + 64 || !text.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            var body = text[(newline+1)..];
            if (!Valid(body) || !string.Equals(text.Substring(Prefix.Length,64), Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(body))), StringComparison.Ordinal)) return false;
            ini = body; return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }
    /// <summary>Atomically replaces only this app settings file. Failure preserves the previous file where possible.</summary>
    public bool TryWrite(string ini)
    {
        string? temporary = null;
        try
        {
            if (!Valid(ini)) return false;
            var full = System.IO.Path.GetFullPath(Path); var directory = System.IO.Path.GetDirectoryName(full)!;
            Directory.CreateDirectory(directory);
            temporary = System.IO.Path.Combine(directory, ".bed-layout-" + Guid.NewGuid().ToString("N") + ".tmp");
            var body = Utf8.GetBytes(ini);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Utf8.GetBytes(Prefix + Convert.ToHexStringLower(SHA256.HashData(body)) + "\n"));
                stream.Write(body); stream.Flush(true);
            }
            File.Move(temporary, full, true); temporary = null; return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
        finally { if (temporary is not null) try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    /// <summary>Loads a validated snapshot into the current ImGui context before its first frame.</summary>
    public bool LoadCurrent()
    {
        if (!TryRead(out var ini)) return false;
        ImGui.LoadIniSettingsFromMemory(ini); return true;
    }
    /// <summary>Captures current docking/window settings without enabling ImGui's implicit file writer.</summary>
    public bool SaveCurrent()
    {
        var ini = ImGui.SaveIniSettingsToMemory();
        return ini.Length <= MaxBytes && TryWrite(ini.ToString() ?? "");
    }
    private static bool Valid(string text) => !string.IsNullOrWhiteSpace(text) && text.Length <= MaxBytes
        && !text.Contains('\0') && text.Contains('[',StringComparison.Ordinal) && Utf8.GetByteCount(text) <= MaxBytes;
}
