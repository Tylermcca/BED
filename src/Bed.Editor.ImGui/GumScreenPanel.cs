using System.Diagnostics;
using Bed.Editor.Gum;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Im = Brutal.ImGuiApi.ImGui;

namespace Bed.Editor.ImGui;

// GUM-PICTURE: the "Gum screen" panel. Gum draws the screen (GumScreenPicture); this panel only lists the project's
// screens (GumCatalog), offers the chosen screen's looks, and shows the pixels through ImGui's own texture list
// (PictureTexture).

/// <summary>Shows a live picture of one of the project's Gum screens, as Gum draws it.</summary>
internal sealed class GumScreenPanel(string projectFile, Action? openInGum = null) : IDisposable
{
    /// <summary>The panel's id in every editor.</summary>
    public const string Id = "gum.screen";

    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly PictureTexture texture = new();
    private GumProject? catalog;
    private GumScreenPicture? picture;
    private bool listOpen, disposed;
    private int savesSeen;
    private double updatedAt = double.NegativeInfinity;

    /// <summary>The Gum project shown.</summary>
    public string ProjectFile => projectFile;

    /// <summary>The picture being shown, if a screen has been chosen.</summary>
    internal GumScreenPicture? Picture => picture;

    /// <summary>The picture as ImGui holds it, for tests.</summary>
    internal PictureTexture Texture => texture;

    /// <summary>The panel, for an editor's panel list.</summary>
    public Panel AsPanel() => new(Id, "Gum screens", Draw, OpenAtStart: false, Width: 640, Height: 520);

    /// <summary>Shows <paramref name="screen"/> in <paramref name="look"/> (null for its default look).</summary>
    internal bool Choose(string screen, GumLook? look = null)
    {
        picture ??= new GumScreenPicture(projectFile);
        return picture.Show(screen, look);
    }

    /// <summary>Draws the panel's choices and the picture, fitted to the panel.</summary>
    public void Draw()
    {
        PictureTexture.Tick();
        if (disposed) { Ui.TextWrapped("The Gum screen picture closed with its project."u8); return; }
        catalog ??= GumCatalog.Read(projectFile);
        if (catalog.Screens.Count == 0)
        {
            Ui.TextWrapped(catalog.Findings.FirstOrDefault()?.Problem.Message ?? "This Gum project has no screens.");
            return;
        }
        if (picture?.Screen is null) Choose(catalog.Screens.FirstOrDefault(s => !s.Missing)?.Name ?? catalog.Screens[0].Name);

        Im.SetNextItemWidth(MathF.Min(260, MathF.Max(120, Im.GetContentRegionAvail().X * 0.45f)));
        var open = Ui.BeginCombo("Screen"u8, picture!.Screen ?? "");
        // The list is read again once each time it opens, to pick up screens added in Gum since; not every frame.
        if (open && !listOpen) catalog = GumCatalog.Read(projectFile);
        listOpen = open;
        if (open)
        {
            foreach (var screen in catalog.Screens)
            {
                var label = screen.Missing ? $"{screen.Name} (file missing)" : screen.Name;
                if (Ui.Selectable(label, screen.Name == picture.Screen)) Choose(screen.Name);
            }
            Ui.EndCombo();
        }
        if (picture.Looks.Count > 0)
        {
            Im.SameLine();
            Im.SetNextItemWidth(MathF.Min(220, MathF.Max(100, Im.GetContentRegionAvail().X * 0.6f)));
            if (Ui.BeginCombo("Look"u8, picture.Look?.Label ?? "Default look"))
            {
                if (Ui.Selectable("Default look"u8, picture.Look is null)) Choose(picture.Screen!);
                foreach (var look in picture.Looks)
                    if (Ui.Selectable(look.Label, look == picture.Look)) Choose(picture.Screen!, look);
                Ui.EndCombo();
            }
        }
        if (openInGum is not null)
        {
            Im.SameLine();
            if (Ui.Button("Edit in Gum"u8)) openInGum();
        }

        var now = clock.Elapsed.TotalSeconds;
        picture.Frame(now);
        if (picture.Saves != savesSeen) { savesSeen = picture.Saves; updatedAt = now; }
        Ui.TextWrapped(now - updatedAt < 4
            ? "Updated from Gum just now."
            : "Change these screens in Gum. When you save there, they update here and in your game.");
        if (picture.Problem.Length > 0) Ui.TextWrapped(picture.Problem);
        if (!picture.Drawn || picture.Pixels.Length == 0) return;
        texture.Update(picture.Width, picture.Height, picture.Pixels, picture.Version);

        // The picture fills the space left, keeping the screen's proportions, centred.
        var room = Im.GetContentRegionAvail();
        var scale = MathF.Max(0.01f, MathF.Min(room.X / picture.Width, room.Y / picture.Height));
        var size = new float2(MathF.Max(1, picture.Width * scale), MathF.Max(1, picture.Height * scale));
        var at = Im.GetCursorScreenPos() + new float2(MathF.Max(0, (room.X - size.X) / 2), 0);
        Im.SetCursorScreenPos(at);
        Im.InvisibleButton("##gum-screen-picture"u8, size);
        Ui.RecordCanvas($"Picture of the Gum screen {picture.Screen}" + (picture.Look is null ? "" : $", look {picture.Look.Label}"));
        if (Im.IsItemVisible()) Im.GetWindowDrawList().AddImage(texture.Ref, at, at + size);
    }

    /// <summary>Lets go of Gum (its watcher stops), the picture's canvas and its ImGui texture.</summary>
    public void Dispose()
    {
        disposed = true;
        picture?.Dispose();
        picture = null;
        texture.Dispose();
    }
}
