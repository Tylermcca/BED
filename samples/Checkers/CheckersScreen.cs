using Bed.Editor.Gum;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Checkers;

// Checkers' own screens, made in Gum (CheckersProject/ui/CheckersUi.gumj, open it in Gum to change them): a title before a match, and a
// strip above the board during one. Gum draws them (BED's GumScreenPicture, Gum's own Skia drawing) and ImGui shows the
// picture, as BED's Gum screen panel does. Saving a screen in Gum while the game runs redraws it.

/// <summary>One of the game's Gum screens, drawn into the current ImGui window at its full width and at the game's text
/// size: bigger text draws the screen bigger, and the screen lays itself out in the room that leaves (Gum's own zoom).
/// UI thread only.</summary>
public sealed class CheckersScreen : IDisposable
{
    private readonly GumScreenPicture picture;
    private readonly PictureTexture texture = new();
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly bool fill;

    /// <summary>The screen <paramref name="screen"/> of <paramref name="projectFile"/>. With <paramref name="fill"/> it
    /// fills the room left in the window; otherwise it is as tall as what's on it (a strip above the board).</summary>
    public CheckersScreen(string projectFile, string screen, bool fill)
    {
        this.fill = fill;
        picture = new GumScreenPicture(projectFile, 720, 400);
        picture.Show(screen);
    }

    /// <summary>What's wrong, in plain words, or empty.</summary>
    public string Problem => picture.Problem;

    /// <summary>Sets a value on the screen, as <see cref="GumScreenPicture.Set"/> does.</summary>
    public void Set(string path, object? value) => picture.Set(path, value);

    /// <summary>The Gum picture itself: its pixels, for checking what it looks like.</summary>
    public GumScreenPicture Picture => picture;

    /// <summary>The screen's parts, in the window's pixels from the top left of the screen as last drawn.</summary>
    public IReadOnlyList<GumPart> Parts() => picture.Parts();

    /// <summary>The text size the screen was last drawn at.</summary>
    public float Zoom => picture.Zoom;

    /// <summary>Draws the screen as wide as the window allows, leaving <paramref name="below"/> pixels under it, and
    /// returns the name of the button pressed this frame, or null. Each Gum part whose name ends in "Button" becomes an
    /// ImGui button over its picture, named by its words, so the mouse, the keyboard and screen readers reach it as they
    /// reach any other button. Shows the problem instead when Gum can't draw the screen.</summary>
    public string? Draw(float below = 0)
    {
        PictureTexture.Tick();
        var style = ImGui.GetStyle();
        var zoom = MathF.Max(0.25f, style.FontScaleMain * style.FontScaleDpi);
        var room = ImGui.GetContentRegionAvail();
        var width = (int)MathF.Max(1, room.X);
        // A strip gets room to wrap onto more lines; only the part with something on it is shown.
        var height = fill ? (int)MathF.Max(1, room.Y - below) : (int)MathF.Ceiling(400 * zoom);
        picture.Resize(width, height, zoom);
        picture.Frame(clock.Elapsed.TotalSeconds);
        if (!picture.Drawn || picture.Pixels.Length == 0)
        {
            ImGui.TextWrapped(picture.Problem);
            return null;
        }
        texture.Update(picture.Width, picture.Height, picture.Pixels, picture.Version);
        var parts = picture.Parts();
        var used = fill ? picture.Height : MathF.Min(picture.Height, MathF.Ceiling(parts.Count == 0 ? picture.Height : parts.Max(p => p.Y + p.Height)));
        var size = new float2(picture.Width, used);
        var at = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddImage(texture.Ref, at, at + size, new float2(0, 0), new float2(1, used / picture.Height));
        ImGui.Dummy(size);
        var after = ImGui.GetCursorScreenPos();
        string? pressed = null;
        foreach (var part in parts)
        {
            if (!part.Name.EndsWith("Button", StringComparison.Ordinal) || part.Words is null) continue;
            ImGui.SetCursorScreenPos(at + new float2(part.X, part.Y));
            if (ImGui.InvisibleButton(part.Words, new float2(MathF.Max(1, part.Width), MathF.Max(1, part.Height)), ImGuiButtonFlags.EnableNav)) pressed = part.Name;
            if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        ImGui.SetCursorScreenPos(after);
        return pressed;
    }

    /// <summary>Lets go of Gum and the picture.</summary>
    public void Dispose()
    {
        picture.Dispose();
        texture.Dispose();
    }

    /// <summary>The game's Gum project next to the program (copied there when the game is built), or null when it isn't
    /// there; the game then uses its plain screen.</summary>
    public static string? Project()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "ui", "CheckersUi.gumj");
        return File.Exists(file) ? file : null;
    }
}
