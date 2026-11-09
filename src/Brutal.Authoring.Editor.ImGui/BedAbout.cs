using Brutal.ImGuiApi;

namespace Brutal.Authoring.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>The shared BED About content and its existing panel/palette entry.</summary>
public static class BedAbout
{
    /// <summary>Product and provisional build version.</summary>
    public static string Heading => "BED " + BuildVersion.Current;
    /// <summary>Full About text; existing BRUTAL credit is reused verbatim, not replaced.</summary>
    public static string Text => Heading + "\nCopyright (c) 2026 Tyler McCauley. MIT licence.\n" +
        BrutalCredit.Copyright + "\nSee LICENSE.txt and NOTICE.txt beside this program.\nLicences folder: " +
        Path.Combine(AppContext.BaseDirectory, "licenses");
    /// <summary>An initially closed About panel; the normal palette exposes editor.panel.bed.about.</summary>
    public static Panel Panel => new("bed.about", "About BED", Draw, OpenAtStart: false, Width: 460, Height: 220);
    /// <summary>Draws the same content for the panel and start-screen About entry.</summary>
    public static void Draw() => Ui.TextWrapped(Text);
}
