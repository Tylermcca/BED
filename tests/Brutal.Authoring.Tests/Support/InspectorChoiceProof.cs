using System;
using System.Linq;
using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;

namespace Brutal.Authoring.TestSupport;

// Test-only source link: compile this file in the consuming test assembly; no test-assembly reference is needed.
// Requires the normal Data/Editor/Editor.ImGui libraries, not xUnit, samples, adapters or a package of test utilities.
// BORROWED from the real-input dropdown proof. The caller owns one serial HeadlessHost, its configured window,
// selection and draw callback. This helper neither owns/disposes the host nor edits the model directly.
internal sealed class InspectorChoiceProof(HeadlessHost host, EditorWindow<DataDocument> window, Action draw)
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // String choices only. Unknown/missing current values, mixed/multiple selection and ambiguous paths fail before
    // input. Popup navigation from no current selection is deliberately unsupported, rather than guessed.
    internal void Choose(string value, params string[] path)
    {
        Require(path.Length > 0, "A choice needs a nonempty inspector label path.");
        var inspector = window.Inspector ?? throw new InvalidOperationException("No inspector is attached.");
        Require(window.Editor.Selection.Count == 1, "Choose requires exactly one selected object.");
        var rows = inspector.Rows();
        InspectorRow? row = null;
        foreach (var label in path)
        {
            var matches = rows.Where(r => r.Label == label).ToList();
            Require(matches.Count == 1, $"Expected one inspector row labelled '{label}', found {matches.Count}.");
            row = matches[0];
            rows = inspector.Nested(row);
        }
        var chosen = row!;
        Require(chosen.Kind == PropertyKind.Choice && chosen.Editable && !chosen.Mixed,
            $"'{chosen.Label}' is not one editable choice.");
        var choices = chosen.Choices.ToList(); // exactly the model order rendered by InspectorController
        Require(choices.Distinct(StringComparer.Ordinal).Count() == choices.Count,
            $"'{chosen.Label}' has duplicate displayed choices; navigation would be ambiguous.");
        var wanted = choices.IndexOf(value);
        Require(wanted >= 0, $"'{value}' is not a declared choice for '{chosen.Label}'.");
        var place = window.Editor.Selection.Single() + DataEditing.PlaceOfRow(chosen.Id);
        var before = window.Editor.Session.Current.At(place);
        Require(before is { ValueKind: JsonValueKind.String }, $"'{place}' has no selected string choice.");
        var current = before!.Value.GetString();
        var selected = choices.IndexOf(current!);
        Require(selected >= 0 && chosen.Value?.ToString() == current,
            $"'{place}' has no selected declared choice matching the inspector.");
        var undoBefore = window.Editor.Session.History.UndoCount;
        host.Frames(draw, 2);
        var at = window.InspectorPlace(chosen.Id, InspectorPart.Field);
        var point = at ?? throw new InvalidOperationException($"'{chosen.Label}' has no field drawn.");
        // Caller must expose the field. Do not pretend a clipped or off-screen click reached its control.
        Require(point.X >= 0 && point.X + 12 < host.Width && point.Y >= 0 && point.Y + 7 < host.Height,
            $"'{chosen.Label}' is outside the host; expose the field before choosing.");
        host.Click(draw, point.X + 12, point.Y + 7);
        host.Frames(draw, 3);
        // Opening the combo places navigation on the selected item (the existing real-input proof contract).
        var steps = wanted - selected;
        for (var i = 0; i < Math.Abs(steps); i++)
        {
            host.Press(draw, steps > 0 ? ImGuiKey.DownArrow : ImGuiKey.UpArrow);
            host.Frames(draw, 2);
        }
        host.Press(draw, ImGuiKey.Enter);
        host.Frames(draw, 3);
        var actual = window.Editor.Session.Current.At(place);
        Require(actual is { ValueKind: JsonValueKind.String } && actual.Value.GetString() == value,
            $"Choosing '{value}' did not reach '{place}'; got {actual?.ToString() ?? "(missing)"}.");
        if (wanted == selected)
            Require(window.Editor.Session.History.UndoCount == undoBefore, "Choosing the current value added an undo step.");
    }
}
