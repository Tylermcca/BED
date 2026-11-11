using Brutal.ImGuiApi;

namespace Bed.Editor.App;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>Which typeface the window's text is drawn with.</summary>
internal enum EditorFontSource
{
    /// <summary>Windows' own interface font (Segoe UI), read from the system fonts folder.</summary>
    Interface,
    /// <summary>ImGui's built-in pixel font, used when the interface font isn't there or won't load.</summary>
    Default,
}

/// <summary>
/// Picks and loads the window's typeface. ImGui's built-in font is a 13-pixel bitmap font; stretched to BED's text
/// sizes it looked blurry and uneven. The window uses Windows' interface font instead, read from the system at run
/// time (it isn't ours to ship), so ImGui draws it sharp at every text size and display scale. When the font file is
/// missing or won't load, the window keeps ImGui's built-in font. The headless test host stays on the built-in font
/// so its pictures and measurements don't depend on the machine. INVENTED.
/// </summary>
internal static class EditorFonts
{
    /// <summary>Segoe UI's file name in the Windows fonts folder.</summary>
    public const string InterfaceFontFile = "segoeui.ttf";

    /// <summary>The interface font's full path under <paramref name="fontsFolder"/>, or null when there's no folder
    /// or <paramref name="exists"/> says the file isn't there.</summary>
    public static string? InterfaceFontPath(string? fontsFolder, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(fontsFolder)) return null;
        var path = Path.Combine(fontsFolder, InterfaceFontFile);
        return exists(path) ? path : null;
    }

    /// <summary>Adds the window's main font to <paramref name="atlas"/>: the interface font at the style's base size
    /// when it can be found and loaded, otherwise ImGui's built-in font exactly as before. Call once, before the
    /// first frame.</summary>
    public static EditorFontSource Load(ImFontAtlasPtr atlas, string? fontsFolder = null, Func<string, bool>? exists = null)
    {
        fontsFolder ??= Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var path = InterfaceFontPath(fontsFolder, exists ?? Readable);
        if (path is not null)
        {
            var size = ImGui.GetStyle().FontSizeBase;
            var font = atlas.AddFontFromFileTTF(path, size > 0 ? size : 16);
            if (!font.IsNull()) return EditorFontSource.Interface;
        }
        atlas.AddFontDefault();
        return EditorFontSource.Default;
    }

    private static bool Readable(string path)
    {
        try { var file = new FileInfo(path); return file.Exists && file.Length > 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }
}
