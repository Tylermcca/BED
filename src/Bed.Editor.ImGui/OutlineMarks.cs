using Bed.Data;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Bed.Editor.ImGui;

using ImGui = Brutal.ImGuiApi.ImGui;

/// <summary>How an Outliner row relates to a prefab.</summary>
internal enum OutlineMarkKind
{
    /// <summary>The placed copy of a prefab: prefab colour and a small box icon.</summary>
    PrefabCopy,
    /// <summary>A part of a copy that still matches the prefab: dimmer prefab colour, no marker.</summary>
    PrefabPart,
    /// <summary>A part changed in this copy: prefab colour and a filled dot after the name.</summary>
    ChangedPart,
}

/// <summary>An Outliner row's prefab marking. <paramref name="Spoken"/> is added to the accessible name after a comma;
/// <paramref name="Tooltip"/> is shown on hover. Neither is drawn as row text.</summary>
internal readonly record struct OutlineMark(OutlineMarkKind Kind, string Spoken, string Tooltip)
{
    /// <summary>The plain-words marking for one object of a version-two scene, or null for an ordinary object.</summary>
    internal static OutlineMark? ForScene2(Scene2 scene, string id)
    {
        var projection = Scene2ViewProjection.For(scene);
        if (!projection.Resolved.Origins.TryGetValue(id, out var origin) || origin.InstanceKey is not { } key
            || !projection.Instances.TryGetValue(key, out var instance)) return null;
        var prefab = PrefabName(scene, instance);
        switch (origin.Kind)
        {
            case Scene2OriginKind.Placement:
                return new(OutlineMarkKind.PrefabCopy, "copy of " + prefab, "Copy of " + prefab);
            case Scene2OriginKind.PrefabNode:
                var fields = instance.Overrides.Where(o => o.Node == origin.NodeKey).Select(o => FieldName(o.Field)).Distinct(StringComparer.Ordinal).ToArray();
                return fields.Length == 0
                    ? new(OutlineMarkKind.PrefabPart, $"part of {prefab} copy", $"Part of {prefab} copy, unchanged")
                    : new(OutlineMarkKind.ChangedPart, "changed in this copy", "Changed in this copy: " + string.Join(", ", fields));
            default:
                return null;
        }
    }

    private static string PrefabName(Scene2 scene, PrefabInstance instance)
    {
        var definition = scene.Bases.FirstOrDefault(b => b.Id == instance.Base)?.Definition;
        var root = definition?.Nodes.FirstOrDefault(n => n.Key == definition.Root);
        return root is { Label.Length: > 0 } ? root.Label : instance.Base;
    }

    // "/position/0" reads "position X"; "/properties/locked" reads "locked".
    internal static string FieldName(string pointer)
    {
        var parts = pointer.TrimStart('/').Split('/').Select(p => p.Replace("~1", "/").Replace("~0", "~")).ToArray();
        if (parts.Length == 0 || parts[0].Length == 0) return pointer;
        if (parts.Length == 2 && parts[0] is "position" or "rotation" or "scale" or "size" or "pivot" && int.TryParse(parts[1], out var axis) && axis is >= 0 and < 4)
            return parts[0] + " " + "XYZW"[axis];
        if (parts.Length >= 2 && parts[0] == "properties") return string.Join("/", parts[1..]);
        if (parts.Length >= 2 && parts[0] == "references") return string.Join("/", parts[1..]) + " reference";
        return string.Join(" ", parts);
    }

    /// <summary>Leading spaces that keep room for the copy icon inside the row's own text.</summary>
    internal static string IconRoom()
    {
        var space = MathF.Max(1, ImGui.CalcTextSize(" "u8).X);
        return new string(' ', (int)MathF.Ceiling(ImGui.GetFontSize() * 1.1f / space));
    }

    internal float4 TextColour => Kind == OutlineMarkKind.PrefabPart ? EditorTheme.PrefabPartText : EditorTheme.PrefabText;

    /// <summary>Draws the icon or dot for a row whose text started at <paramref name="origin"/>.</summary>
    internal void Draw(float2 origin, float iconX, float nameEndX)
    {
        var draw = ImGui.GetWindowDrawList(); var font = ImGui.GetFontSize();
        if (Kind == OutlineMarkKind.PrefabCopy)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, TextColour); var ink = ImGui.GetColorU32(ImGuiCol.Text); ImGui.PopStyleColor();
            // A small box seen from above a corner: a hexagon outline with the three inner edges.
            var r = font * 0.42f; var c = new float2(iconX + r + font * 0.05f, origin.Y + font * 0.5f); var w = r * 0.866f; var t = MathF.Max(1, font / 14f);
            float2 top = c + new float2(0, -r), upRight = c + new float2(w, -r / 2), downRight = c + new float2(w, r / 2),
                bottom = c + new float2(0, r), downLeft = c + new float2(-w, r / 2), upLeft = c + new float2(-w, -r / 2);
            draw.AddLine(top, upRight, ink, t); draw.AddLine(upRight, downRight, ink, t); draw.AddLine(downRight, bottom, ink, t);
            draw.AddLine(bottom, downLeft, ink, t); draw.AddLine(downLeft, upLeft, ink, t); draw.AddLine(upLeft, top, ink, t);
            draw.AddLine(c, upLeft, ink, t); draw.AddLine(c, upRight, ink, t); draw.AddLine(c, bottom, ink, t);
        }
        else if (Kind == OutlineMarkKind.ChangedPart)
        {
            var r = MathF.Max(2, font * 0.18f);
            var x = MathF.Min(nameEndX + font * 0.35f + r, ImGui.GetItemRectMax().X - r - 2);
            draw.AddCircleFilled(new float2(x, origin.Y + font * 0.5f), r, ImGui.GetColorU32(ImGuiCol.CheckMark));
        }
    }
}
