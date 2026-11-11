using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ThemeA454Tests
{
    private static readonly ImGuiCol[] Surfaces =
    [
        ImGuiCol.WindowBg, ImGuiCol.ChildBg, ImGuiCol.PopupBg, ImGuiCol.MenuBarBg,
        ImGuiCol.Button, ImGuiCol.ButtonHovered, ImGuiCol.ButtonActive,
        ImGuiCol.Header, ImGuiCol.HeaderHovered, ImGuiCol.HeaderActive,
        ImGuiCol.Tab, ImGuiCol.TabHovered, ImGuiCol.TabSelected,
        ImGuiCol.TabDimmed, ImGuiCol.TabDimmedSelected,
        ImGuiCol.FrameBg, ImGuiCol.FrameBgHovered, ImGuiCol.FrameBgActive,
        ImGuiCol.TitleBg, ImGuiCol.TitleBgActive, ImGuiCol.TableHeaderBg,
        ImGuiCol.TableRowBg, ImGuiCol.TableRowBgAlt,
    ];

    // The same WCAG relative-luminance calculation used by EditorStyleTests.
    private static double Contrast(float4 a, float4 b)
    {
        static double Luminance(float4 c)
        {
            static double Channel(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            return 0.2126 * Channel(c.X) + 0.7152 * Channel(c.Y) + 0.0722 * Channel(c.Z);
        }
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Theory]
    [InlineData(EditorThemeAppearance.Light)]
    [InlineData(EditorThemeAppearance.Dark)]
    public void Both_themes_have_AA_text_focus_and_frame_boundaries(EditorThemeAppearance appearance)
    {
        using var host = new HeadlessHost(200, 100);
        EditorTheme.Apply(appearance);
        var colours = ImGui.GetStyle().Colors;
        foreach (var surface in Surfaces)
        {
            var background = colours[(int)surface];
            Assert.Equal(1f, background.W);
            Assert.True(Contrast(colours[(int)ImGuiCol.Text], background) >= 4.5, $"{appearance}/{surface} text");
            Assert.True(Contrast(colours[(int)ImGuiCol.TextDisabled], background) >= 4.5, $"{appearance}/{surface} muted text");
            Assert.True(Contrast(colours[(int)ImGuiCol.NavCursor], background) >= 3, $"{appearance}/{surface} focus");
        }
        Assert.True(ImGui.GetStyle().FrameBorderSize >= 1);
        foreach (var surface in new[] { ImGuiCol.WindowBg, ImGuiCol.FrameBg })
            Assert.True(Contrast(colours[(int)ImGuiCol.Border], colours[(int)surface]) >= 3, $"{appearance}/{surface} border");
    }

    [Theory]
    [InlineData(EditorThemeAppearance.Light)]
    [InlineData(EditorThemeAppearance.Dark)]
    public void Switching_theme_preserves_both_text_scales(EditorThemeAppearance appearance)
    {
        using var host = new HeadlessHost(200, 100);
        var style = ImGui.GetStyle();
        style.FontScaleMain = 1.75f;
        style.FontScaleDpi = 1.5f;
        EditorTheme.Apply(appearance);
        Assert.Equal(1.75f, style.FontScaleMain);
        Assert.Equal(1.5f, style.FontScaleDpi);
        Assert.InRange(style.FrameRounding, 2f, 4f);
        Assert.True(style.FramePadding.Y >= 4);
    }

    [Theory]
    [InlineData(null, EditorThemeAppearance.Dark)]
    [InlineData(0, EditorThemeAppearance.Dark)]
    [InlineData(1, EditorThemeAppearance.Light)]
    [InlineData(2, EditorThemeAppearance.Dark)]
    [InlineData(-1, EditorThemeAppearance.Dark)]
    public void System_choice_resolves_the_Windows_app_setting(int? appMode, EditorThemeAppearance expected)
        => Assert.Equal(expected, EditorTheme.Resolve(EditorThemePreference.System, appMode));

    [Theory]
    [InlineData(EditorThemePreference.Light, 0, EditorThemeAppearance.Light)]
    [InlineData(EditorThemePreference.Dark, 1, EditorThemeAppearance.Dark)]
    [InlineData(EditorThemePreference.Light, null, EditorThemeAppearance.Light)]
    [InlineData(EditorThemePreference.Dark, null, EditorThemeAppearance.Dark)]
    public void Explicit_override_wins_over_Windows(EditorThemePreference choice, int? appMode, EditorThemeAppearance expected)
        => Assert.Equal(expected, EditorTheme.Resolve(choice, appMode));

    [Theory]
    [InlineData(EditorThemePreference.Light, EditorThemeAppearance.Light)]
    [InlineData(EditorThemePreference.Dark, EditorThemeAppearance.Dark)]
    public void Existing_style_entry_point_honours_the_override_without_resetting_text(EditorThemePreference choice, EditorThemeAppearance appearance)
    {
        var original = EditorTheme.Preference;
        try
        {
            using var host = new HeadlessHost(200, 100);
            ImGui.GetStyle().FontScaleMain = 1.5f;
            EditorTheme.Preference = choice;
            EditorStyle.Apply();
            var window = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
            if (appearance == EditorThemeAppearance.Light) Assert.True(window.X > 0.9f);
            else Assert.True(window.X < 0.2f);
            Assert.Equal(1.5f, ImGui.GetStyle().FontScaleMain);
        }
        finally { EditorTheme.Preference = original; }
    }
    [Fact]
    public void Frame_refresh_preserves_DPI_scaled_geometry_and_text()
    {
        var original = EditorTheme.Preference;
        try
        {
            using var host = new HeadlessHost(200, 100);
            var style = ImGui.GetStyle();
            style.WindowPadding = new float2(14, 14);
            style.FramePadding = new float2(10.5f, 7);
            style.FontScaleMain = 1.5f;
            style.FontScaleDpi = 1.75f;
            EditorTheme.Preference = EditorThemePreference.Light;
            EditorTheme.Refresh();
            EditorTheme.Refresh();
            Assert.Equal(new float2(14, 14), style.WindowPadding);
            Assert.Equal(new float2(10.5f, 7), style.FramePadding);
            Assert.Equal(1.5f, style.FontScaleMain);
            Assert.Equal(1.75f, style.FontScaleDpi);
        }
        finally { EditorTheme.Preference = original; }
    }
    [Theory]
    [InlineData(EditorThemePreference.Light)]
    [InlineData(EditorThemePreference.Dark)]
    public void Both_themes_keep_the_strict_nested_page_Add_contract(EditorThemePreference choice)
    {
        var original = EditorTheme.Preference;
        try
        {
            EditorTheme.Preference = choice;
            // Reuse the entire existing strict real-input scenario, including count, page and undo assertions.
            new NestedFieldsTests().A_long_list_is_drawn_a_page_at_a_time_and_a_new_item_shows_its_page();
        }
        finally { EditorTheme.Preference = original; }
    }
    [Fact]
    public void Unknown_preferences_are_refused_without_changing_current_choice()
    {
        var original = EditorTheme.Preference;
        Assert.Throws<ArgumentOutOfRangeException>(() => EditorTheme.Preference = (EditorThemePreference)99);
        Assert.Equal(original, EditorTheme.Preference);
        Assert.Throws<ArgumentOutOfRangeException>(() => EditorTheme.Resolve((EditorThemePreference)99, 1));
    }
}
