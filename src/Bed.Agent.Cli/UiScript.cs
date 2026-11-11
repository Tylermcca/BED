using System.Text.Json;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;

namespace Bed.Agent.Cli;

/// <summary>Bounded input for an explicitly owned headless host, never a live-window input service.</summary>
public static class UiScript
{
    private sealed record Step(string Kind, string Text, int Row = -1);

    private static Step[] Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > 32)
            throw new ArgumentException("ui script must be an array of at most 32 steps.");
        return root.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 1)
                throw new ArgumentException("Each ui script step needs exactly one click, type, key or select property.");
            var property = item.EnumerateObject().Single();
            if (property.Name == "select" && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var row) && row >= 0)
                return new Step("select", "", row);
            if (property.Name is not ("click" or "type" or "key") || property.Value.ValueKind != JsonValueKind.String)
                throw new ArgumentException("click/type/key need strings; select needs a zero-based nonnegative row number.");
            var text = property.Value.GetString()!;
            if (text.Length > 4096 || property.Name != "type" && string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Step text must fit 4096 characters; click/key cannot be empty.");
            if (property.Name == "key") Key(text);
            return new Step(property.Name, text);
        }).ToArray();
    }

    /// <summary>Checks the complete script before any host is created or action is applied.</summary>
    public static void Validate(string json) => Parse(json);

    private static (ImGuiKey Key, bool Ctrl, bool Shift, bool Alt) Key(string text)
    {
        var parts = text.Split('+');
        var modifiers = parts[..^1];
        if (modifiers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != modifiers.Length ||
            modifiers.Any(x => !new[] { "Ctrl", "Shift", "Alt" }.Contains(x, StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("Unsupported or repeated key modifier.");
        var name = parts[^1];
        if (!Enum.GetNames<ImGuiKey>().Contains(name, StringComparer.OrdinalIgnoreCase) ||
            !Enum.TryParse<ImGuiKey>(name, true, out var key) ||
            !(name.Length == 1 && char.IsAsciiLetter(name[0]) ||
              new[] { "Tab", "Enter", "Escape", "Space", "Backspace", "Delete", "Insert", "Home", "End", "PageUp", "PageDown", "LeftArrow", "RightArrow", "UpArrow", "DownArrow", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12" }.Contains(name, StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("Unsupported key; use a named keyboard key such as Ctrl+S or Escape.");
        return (key, modifiers.Contains("Ctrl", StringComparer.OrdinalIgnoreCase), modifiers.Contains("Shift", StringComparer.OrdinalIgnoreCase), modifiers.Contains("Alt", StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Applies validated steps to this host, returning each completed snapshot to the caller. Refusal stops remaining steps.</summary>
    public static void Run(string json, HeadlessHost host, Action draw, Action<UiSnapshot> emit)
    {
        var steps = Parse(json);
        foreach (var step in steps)
        {
            var nodes = Ui.Snapshot().Nodes;
            switch (step.Kind)
            {
                case "click":
                    var matches = nodes.Where(n => n.Label == step.Text && n.Actions != UiActions.None).ToArray();
                    if (matches.Length != 1) throw new ArgumentException("Click target is missing or ambiguous: " + step.Text);
                    Click(matches[0], host, draw);
                    break;
                case "select":
                    var rows = nodes.Where(n => n.Role is UiRole.Row or UiRole.ListItem && n.Actions.HasFlag(UiActions.Select)).ToArray();
                    if (rows.Select(n => n.Parent).Distinct().Count() != 1 || step.Row >= rows.Length)
                        throw new ArgumentException("Select needs one recorded row list and an in-range zero-based row.");
                    Click(rows[step.Row], host, draw);
                    break;
                case "type":
                    var fields = nodes.Where(n => n.Focused && n.Role == UiRole.TextField && n.Enabled && n.Actions.HasFlag(UiActions.SetValue)).ToArray();
                    if (fields.Length != 1 || !ImGui.GetIO().WantTextInput)
                        throw new ArgumentException("Type needs one focused editable recorded text field.");
                    host.Type(draw, step.Text);
                    break;
                case "key":
                    var key = Key(step.Text);
                    host.Press(draw, key.Key, key.Ctrl, key.Shift, key.Alt);
                    break;
            }
            host.Frames(draw, 2);
            emit(Ui.Snapshot());
        }
    }

    private static void Click(UiNode node, HeadlessHost host, Action draw)
    {
        var b = node.Bounds;
        if (!node.Enabled) throw new ArgumentException("Target disabled: " + (node.DisabledReason ?? node.Label));
        if (b.IsEmpty || !float.IsFinite(b.X + b.Y + b.Width + b.Height) || b.X < 0 || b.Y < 0 || b.X + b.Width > host.Width || b.Y + b.Height > host.Height)
            throw new ArgumentException("Target has no fully in-viewport recorded hit bounds.");
        host.Click(draw, b.X + b.Width / 2, b.Y + b.Height / 2);
    }
}
