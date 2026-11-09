using Brutal.ImGuiApi;

namespace Brutal.Authoring.Editor.App;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>Per-context display scaling from original style metrics, separate from BED's saved text-size preference.</summary>
public sealed unsafe class DpiScale
{
    private readonly ImGuiStyle baseline;
    private float current = 1;

    /// <summary>Captures size metrics after host configuration, before any display scaling.</summary>
    public DpiScale() => baseline = *ImGui.PInvoke.GetStyle();

    /// <summary>The last valid display scale applied by this instance.</summary>
    public float Current => current;

    /// <summary>Applies a finite positive GLFW content scale before NewFrame. Invalid values leave the current scale intact.</summary>
    public bool Apply(float scale)
    {
        if (!float.IsFinite(scale) || scale <= 0 || scale == current) return false;
        var scaled = baseline;
        new ImGuiStylePtr(&scaled).ScaleAllSizes(scale);
        var style = ImGui.GetStyle();
        // Copy dimensions only. Theme colors, alignment, alpha, timing and the person's FontScaleMain stay live.
        style.WindowPadding = scaled.WindowPadding;
        style.WindowRounding = scaled.WindowRounding;
        style.WindowBorderSize = scaled.WindowBorderSize;
        style.WindowBorderHoverPadding = scaled.WindowBorderHoverPadding;
        style.WindowMinSize = scaled.WindowMinSize;
        style.ChildRounding = scaled.ChildRounding;
        style.ChildBorderSize = scaled.ChildBorderSize;
        style.PopupRounding = scaled.PopupRounding;
        style.PopupBorderSize = scaled.PopupBorderSize;
        style.FramePadding = scaled.FramePadding;
        style.FrameRounding = scaled.FrameRounding;
        style.FrameBorderSize = scaled.FrameBorderSize;
        style.ItemSpacing = scaled.ItemSpacing;
        style.ItemInnerSpacing = scaled.ItemInnerSpacing;
        style.CellPadding = scaled.CellPadding;
        style.TouchExtraPadding = scaled.TouchExtraPadding;
        style.IndentSpacing = scaled.IndentSpacing;
        style.ColumnsMinSpacing = scaled.ColumnsMinSpacing;
        style.ScrollbarSize = scaled.ScrollbarSize;
        style.ScrollbarRounding = scaled.ScrollbarRounding;
        style.GrabMinSize = scaled.GrabMinSize;
        style.GrabRounding = scaled.GrabRounding;
        style.LogSliderDeadzone = scaled.LogSliderDeadzone;
        style.ImageBorderSize = scaled.ImageBorderSize;
        style.TabRounding = scaled.TabRounding;
        style.TabBorderSize = scaled.TabBorderSize;
        style.TabMinWidthBase = scaled.TabMinWidthBase;
        style.TabMinWidthShrink = scaled.TabMinWidthShrink;
        style.TabCloseButtonMinWidthSelected = scaled.TabCloseButtonMinWidthSelected;
        style.TabCloseButtonMinWidthUnselected = scaled.TabCloseButtonMinWidthUnselected;
        style.TabBarBorderSize = scaled.TabBarBorderSize;
        style.TabBarOverlineSize = scaled.TabBarOverlineSize;
        style.TreeLinesSize = scaled.TreeLinesSize;
        style.TreeLinesRounding = scaled.TreeLinesRounding;
        style.SeparatorTextBorderSize = scaled.SeparatorTextBorderSize;
        style.SeparatorTextPadding = scaled.SeparatorTextPadding;
        style.DisplayWindowPadding = scaled.DisplayWindowPadding;
        style.DisplaySafeAreaPadding = scaled.DisplaySafeAreaPadding;
        style.DockingSeparatorSize = scaled.DockingSeparatorSize;
        style.MouseCursorScale = scaled.MouseCursorScale;
        style.FontScaleDpi = scale;
        current = scale;
        return true;
    }
}