using System.Security;
using Microsoft.Win32;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Bed.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>The person's app theme choice; System follows Windows high contrast, then the app-mode setting.</summary>
public enum EditorThemePreference
{
    /// <summary>Follow Windows high contrast and app mode; use dark when app mode cannot be read.</summary>
    System,
    /// <summary>Use the light palette.</summary>
    Light,
    /// <summary>Use the dark palette.</summary>
    Dark,
    /// <summary>Opaque high-contrast surfaces, white text and yellow focus accents.</summary>
    HighContrast,
}

/// <summary>The resolved palette, independent of where a preference was stored.</summary>
public enum EditorThemeAppearance
{
    /// <summary>Dark text on light neutral surfaces.</summary>
    Light,
    /// <summary>Light text on dark neutral surfaces.</summary>
    Dark,
    /// <summary>Opaque high-contrast surfaces, white text and yellow focus accents.</summary>
    HighContrast,
}

/// <summary>Local UI-thread theme selection and application. It never changes Windows settings or text scale.</summary>
public static class EditorTheme
{
    private static EditorThemePreference preference;
    private static long nextSystemRead = long.MinValue;
    private static int? cachedAppMode;
    private static bool? cachedHighContrast;
    private static Func<long> systemTicks = () => Environment.TickCount64;
    // Internal injected readers keep headless tests independent of the machine's personal settings.
    private static Func<int?> readAppMode = ReadWindowsAppMode;
    private static Func<bool?> readHighContrast = WindowsHighContrast.Read;
    internal static bool HighContrastActive => CurrentAppearance() == EditorThemeAppearance.HighContrast;

    /// <summary>The app's current choice. Persistence belongs to the host's preferences store.</summary>
    public static EditorThemePreference Preference
    {
        get => preference;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            preference = value;
            nextSystemRead = long.MinValue;
        }
    }

    /// <summary>Resolves an override or a Windows AppsUseLightTheme value; missing or invalid system values use dark.</summary>
    public static EditorThemeAppearance Resolve(EditorThemePreference choice, int? appsUseLightTheme) => choice switch
    {
        EditorThemePreference.Light => EditorThemeAppearance.Light,
        EditorThemePreference.Dark => EditorThemeAppearance.Dark,
        EditorThemePreference.HighContrast => EditorThemeAppearance.HighContrast,
        EditorThemePreference.System => appsUseLightTheme == 1 ? EditorThemeAppearance.Light : EditorThemeAppearance.Dark,
        _ => throw new ArgumentOutOfRangeException(nameof(choice)),
    };

    /// <summary>Reads the Windows app-mode value without writing it; null means absent, inaccessible or unsupported.</summary>
    public static int? ReadWindowsAppMode()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value ? value : null;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>Applies the current choice, checking cached Windows high contrast and app mode at most once per second.</summary>
    public static void ApplyCurrent() => Apply(CurrentAppearance());

    /// <summary>Refreshes colours for an editor frame without resetting DPI-scaled spacing or either text scale.</summary>
    public static void Refresh() => ApplyColours(CurrentAppearance());

    private static EditorThemeAppearance CurrentAppearance()
    {
        if (preference == EditorThemePreference.System && systemTicks() >= nextSystemRead)
        {
            cachedAppMode = readAppMode();
            cachedHighContrast = readHighContrast();
            nextSystemRead = systemTicks() + 1000;
        }
        return preference == EditorThemePreference.System && cachedHighContrast == true
            ? EditorThemeAppearance.HighContrast : Resolve(preference, cachedAppMode);
    }

    /// <summary>Applies opaque surfaces to the current ImGui context; HighContrast uses white text and yellow accents.</summary>
    public static void Apply(EditorThemeAppearance appearance)
    {
        ApplyColours(appearance);
        var style = ImGui.GetStyle();
        // Set logical geometry once, before the host applies its DPI scale.
        if(style.FontSizeBase is 0 or 13)style.FontSizeBase=16;
        style.WindowPadding = new float2(8, 8);
        style.FramePadding = new float2(6, 4);
        // Fifty-row inspector pages must keep their Add control inside the existing viewport.
        style.ItemSpacing = new float2(8, 4);
        style.ItemInnerSpacing = new float2(6, 4);
        style.CellPadding = new float2(6, 4);
        style.WindowRounding = 0;
        style.ChildRounding = 3;
        style.FrameRounding = 3;
        style.PopupRounding = 3;
        style.ScrollbarRounding = 4;
        style.GrabRounding = 3;
        style.TabRounding = 3;
        style.FrameBorderSize = 1;
    }

    internal static string Label(EditorThemePreference choice) => choice == EditorThemePreference.HighContrast ? "High contrast" : choice.ToString();

    private static void ApplyColours(EditorThemeAppearance appearance)
    {
        if (!Enum.IsDefined(appearance)) throw new ArgumentOutOfRangeException(nameof(appearance));
        var light = appearance == EditorThemeAppearance.Light;
        var high = appearance == EditorThemeAppearance.HighContrast;
        switch (appearance)
        {
            case EditorThemeAppearance.Light: ImGui.StyleColorsLight(); break;
            case EditorThemeAppearance.Dark:
            case EditorThemeAppearance.HighContrast: ImGui.StyleColorsDark(); break;
            default: throw new ArgumentOutOfRangeException(nameof(appearance));
        }
        var style = ImGui.GetStyle();
        var text = Rgb(high ? 0xFFFFFF : light ? 0x1D2329 : 0xE3E7EA);
        var muted = Rgb(high ? 0xE6E6E6 : light ? 0x46525C : 0xB8C2CB);
        var window = Rgb(high ? 0x000000 : light ? 0xF7F8F9 : 0x1B2026);
        var panel = Rgb(high ? 0x000000 : light ? 0xFFFFFF : 0x20262D);
        var chrome = Rgb(high ? 0x151515 : light ? 0xEEF1F3 : 0x262D35);
        var hovered = Rgb(high ? 0x193040 : light ? 0xDDE7F0 : 0x2D3A47);
        var selected = Rgb(high ? 0x003D66 : light ? 0xCFE2F3 : 0x24425E);
        var pressed = Rgb(high ? 0x254555 : light ? 0xB7CEE2 : 0x304E6A);
        var boundary = Rgb(high ? 0xFFFFFF : light ? 0x747E88 : 0x7D8A96);
        var accent = Rgb(high ? 0xFFFF00 : light ? 0x1F6FB2 : 0x5AA2E0);

        style.Colors[(int)ImGuiCol.Text] = text;
        style.Colors[(int)ImGuiCol.TextDisabled] = muted;
        style.Colors[(int)ImGuiCol.WindowBg] = window;
        style.Colors[(int)ImGuiCol.ChildBg] = panel;
        style.Colors[(int)ImGuiCol.PopupBg] = panel;
        style.Colors[(int)ImGuiCol.Border] = boundary;
        style.Colors[(int)ImGuiCol.BorderShadow] = new float4(0, 0, 0, 0);
        style.Colors[(int)ImGuiCol.FrameBg] = panel;
        style.Colors[(int)ImGuiCol.FrameBgHovered] = hovered;
        style.Colors[(int)ImGuiCol.FrameBgActive] = pressed;
        style.Colors[(int)ImGuiCol.TitleBg] = chrome;
        style.Colors[(int)ImGuiCol.TitleBgActive] = selected;
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = chrome;
        style.Colors[(int)ImGuiCol.MenuBarBg] = chrome;
        style.Colors[(int)ImGuiCol.ScrollbarBg] = window;
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = boundary;
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = accent;
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = accent;
        style.Colors[(int)ImGuiCol.CheckMark] = accent;
        style.Colors[(int)ImGuiCol.SliderGrab] = accent;
        style.Colors[(int)ImGuiCol.SliderGrabActive] = accent;
        style.Colors[(int)ImGuiCol.Button] = chrome;
        style.Colors[(int)ImGuiCol.ButtonHovered] = hovered;
        style.Colors[(int)ImGuiCol.ButtonActive] = pressed;
        style.Colors[(int)ImGuiCol.Header] = selected;
        style.Colors[(int)ImGuiCol.HeaderHovered] = hovered;
        style.Colors[(int)ImGuiCol.HeaderActive] = pressed;
        style.Colors[(int)ImGuiCol.Separator] = boundary;
        style.Colors[(int)ImGuiCol.SeparatorHovered] = accent;
        style.Colors[(int)ImGuiCol.SeparatorActive] = accent;
        style.Colors[(int)ImGuiCol.ResizeGrip] = boundary;
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = accent;
        style.Colors[(int)ImGuiCol.ResizeGripActive] = accent;
        style.Colors[(int)ImGuiCol.InputTextCursor] = accent;
        style.Colors[(int)ImGuiCol.Tab] = chrome;
        style.Colors[(int)ImGuiCol.TabHovered] = hovered;
        style.Colors[(int)ImGuiCol.TabSelected] = selected;
        style.Colors[(int)ImGuiCol.TabSelectedOverline] = accent;
        style.Colors[(int)ImGuiCol.TabDimmed] = chrome;
        style.Colors[(int)ImGuiCol.TabDimmedSelected] = selected;
        style.Colors[(int)ImGuiCol.TabDimmedSelectedOverline] = boundary;
        style.Colors[(int)ImGuiCol.DockingPreview] = new float4(accent.X, accent.Y, accent.Z, 0.7f);
        style.Colors[(int)ImGuiCol.DockingEmptyBg] = window;
        style.Colors[(int)ImGuiCol.PlotLines] = accent;
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = accent;
        style.Colors[(int)ImGuiCol.PlotHistogram] = accent;
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = accent;
        style.Colors[(int)ImGuiCol.TableHeaderBg] = chrome;
        style.Colors[(int)ImGuiCol.TableBorderStrong] = boundary;
        style.Colors[(int)ImGuiCol.TableBorderLight] = boundary;
        style.Colors[(int)ImGuiCol.TableRowBg] = panel;
        style.Colors[(int)ImGuiCol.TableRowBgAlt] = chrome;
        style.Colors[(int)ImGuiCol.TextLink] = accent;
        style.Colors[(int)ImGuiCol.TextSelectedBg] = selected;
        style.Colors[(int)ImGuiCol.TreeLines] = boundary;
        style.Colors[(int)ImGuiCol.DragDropTarget] = accent;
        style.Colors[(int)ImGuiCol.NavCursor] = accent;
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = accent;
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new float4(0, 0, 0, 0.2f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new float4(0, 0, 0, high ? 0.75f : light ? 0.3f : 0.55f);

    }

    private static float4 Rgb(int colour) => new(((colour >> 16) & 255) / 255f, ((colour >> 8) & 255) / 255f, (colour & 255) / 255f, 1);
}
