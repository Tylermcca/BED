using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

/// <summary>The window's typeface: Windows' interface font when it's there, ImGui's built-in font otherwise.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class EditorFontsTests
{
    [Fact]
    public void Interface_font_path_is_used_when_the_file_is_there()
    {
        var folder = Path.Combine("C:", "Windows", "Fonts");
        var expected = Path.Combine(folder, "segoeui.ttf");
        Assert.Equal(expected, EditorFonts.InterfaceFontPath(folder, path => path == expected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_fonts_folder_means_no_interface_font(string? folder) =>
        Assert.Null(EditorFonts.InterfaceFontPath(folder, _ => true));

    [Fact]
    public void Missing_interface_font_means_no_path() =>
        Assert.Null(EditorFonts.InterfaceFontPath(@"C:\Windows\Fonts", _ => false));

    [Fact]
    public void Missing_interface_font_falls_back_to_the_built_in_font()
    {
        using var host = new HeadlessHost { Drawing = false };
        var fonts = ImGui.GetIO().Fonts;
        var before = fonts.Fonts.Count;
        var folder = Path.Combine(Path.GetTempPath(), $"bed-no-fonts-{Guid.NewGuid():N}");
        Assert.Equal(EditorFontSource.Default, EditorFonts.Load(fonts, folder));
        Assert.Equal(before + 1, fonts.Fonts.Count);
        host.Frames(() => { ImGui.Begin("Fallback"u8); ImGui.Text("Open project"u8); ImGui.End(); }, 2);
    }

    [Fact]
    public void Interface_font_that_will_not_load_falls_back_to_the_built_in_font()
    {
        var folder = Directory.CreateTempSubdirectory("bed-bad-font-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, EditorFonts.InterfaceFontFile), "not a font"u8.ToArray());
            using var host = new HeadlessHost { Drawing = false };
            Assert.Equal(EditorFontSource.Default, EditorFonts.Load(ImGui.GetIO().Fonts, folder));
            host.Frames(() => { ImGui.Begin("Bad font"u8); ImGui.Text("Open project"u8); ImGui.End(); }, 2);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Windows_interface_font_loads_and_draws_at_the_style_size()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (EditorFonts.InterfaceFontPath(folder, File.Exists) is null) return;   // not a Windows machine with Segoe UI
        using var host = new HeadlessHost { Drawing = false };
        var fonts = ImGui.GetIO().Fonts;
        Assert.Equal(EditorFontSource.Interface, EditorFonts.Load(fonts, folder));
        var segoe = fonts.Fonts[fonts.Fonts.Count - 1];
        float size = 0, width = 0;
        host.Frames(() =>
        {
            ImGui.Begin("Interface font"u8);
            ImGui.PushFont(segoe, 0);
            ImGui.Text("Open project"u8);
            size = ImGui.GetFontSize(); width = ImGui.CalcTextSize("Open project"u8).X;
            ImGui.PopFont();
            ImGui.End();
        }, 2);
        Assert.InRange(size, ImGui.GetStyle().FontSizeBase - 1, ImGui.GetStyle().FontSizeBase + 1);
        Assert.True(width > 0);
    }
}
