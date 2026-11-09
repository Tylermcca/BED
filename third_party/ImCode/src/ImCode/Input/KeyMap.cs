using Brutal.ImGuiApi;

namespace ImCode.Input;

public readonly record struct KeyChord(ImGuiKey Key, bool Ctrl = false, bool Shift = false, bool Alt = false)
{
    public bool IsPressed(ImGuiIOPtr io) =>
        io.KeyCtrl == Ctrl && io.KeyShift == Shift && io.KeyAlt == Alt && ImGui.IsKeyPressed(Key, true);

    public override string ToString()
    {
        var key = Key.ToString().TrimStart('_');
        return $"{(Ctrl ? "Ctrl+" : "")}{(Shift ? "Shift+" : "")}{(Alt ? "Alt+" : "")}{key}";
    }
}

/// <summary>Maps key chords to command ids. Several chords may map to the same command.</summary>
public sealed class KeyMap
{
    readonly Dictionary<KeyChord, string> _bindings = [];

    public IReadOnlyDictionary<KeyChord, string> Bindings => _bindings;

    public void Bind(KeyChord chord, string command) => _bindings[chord] = command;
    public void Unbind(KeyChord chord) => _bindings.Remove(chord);

    public IEnumerable<KeyChord> ChordsFor(string command) =>
        _bindings.Where(b => b.Value == command).Select(b => b.Key);

    /// <summary>Returns the command of the first pressed chord this frame.</summary>
    public string? GetPressedCommand() => TryGetPressed(out _, out var command) ? command : null;

    public bool TryGetPressed(out KeyChord chord, out string command)
    {
        var io = ImGui.GetIO();
        foreach (var (c, cmd) in _bindings)
        {
            if (c.IsPressed(io))
            {
                chord = c;
                command = cmd;
                return true;
            }
        }
        chord = default;
        command = "";
        return false;
    }

    /// <summary>First chord bound to the command, formatted for menus, or null.</summary>
    public string? ShortcutText(string command)
    {
        foreach (var (chord, cmd) in _bindings)
            if (cmd == command)
                return chord.ToString();
        return null;
    }

    public static KeyMap CreateDefault()
    {
        var map = new KeyMap();
        void Move(ImGuiKey key, string command, bool ctrl = false)
        {
            map.Bind(new KeyChord(key, ctrl), command);
            map.Bind(new KeyChord(key, ctrl, Shift: true), command + EditorCommands.SelectSuffix);
        }

        Move(ImGuiKey.LeftArrow, EditorCommands.CaretLeft);
        Move(ImGuiKey.RightArrow, EditorCommands.CaretRight);
        Move(ImGuiKey.LeftArrow, EditorCommands.CaretWordLeft, ctrl: true);
        Move(ImGuiKey.RightArrow, EditorCommands.CaretWordRight, ctrl: true);
        Move(ImGuiKey.UpArrow, EditorCommands.CaretUp);
        Move(ImGuiKey.DownArrow, EditorCommands.CaretDown);
        Move(ImGuiKey.PageUp, EditorCommands.CaretPageUp);
        Move(ImGuiKey.PageDown, EditorCommands.CaretPageDown);
        Move(ImGuiKey.Home, EditorCommands.CaretLineStart);
        Move(ImGuiKey.End, EditorCommands.CaretLineEnd);
        Move(ImGuiKey.Home, EditorCommands.CaretDocumentStart, ctrl: true);
        Move(ImGuiKey.End, EditorCommands.CaretDocumentEnd, ctrl: true);

        map.Bind(new(ImGuiKey.UpArrow, Ctrl: true), EditorCommands.ScrollUp);
        map.Bind(new(ImGuiKey.DownArrow, Ctrl: true), EditorCommands.ScrollDown);

        map.Bind(new(ImGuiKey.Backspace), EditorCommands.Backspace);
        map.Bind(new(ImGuiKey.Backspace, Shift: true), EditorCommands.Backspace);
        map.Bind(new(ImGuiKey.Backspace, Ctrl: true), EditorCommands.BackspaceWord);
        map.Bind(new(ImGuiKey.Delete), EditorCommands.Delete);
        map.Bind(new(ImGuiKey.Delete, Ctrl: true), EditorCommands.DeleteWord);
        map.Bind(new(ImGuiKey.Enter), EditorCommands.NewLine);
        map.Bind(new(ImGuiKey.KeypadEnter), EditorCommands.NewLine);
        map.Bind(new(ImGuiKey.Tab), EditorCommands.Indent);
        map.Bind(new(ImGuiKey.Tab, Shift: true), EditorCommands.Outdent);

        map.Bind(new(ImGuiKey.Z, Ctrl: true), EditorCommands.Undo);
        map.Bind(new(ImGuiKey.Y, Ctrl: true), EditorCommands.Redo);
        map.Bind(new(ImGuiKey.Z, Ctrl: true, Shift: true), EditorCommands.Redo);
        map.Bind(new(ImGuiKey.X, Ctrl: true), EditorCommands.Cut);
        map.Bind(new(ImGuiKey.Delete, Shift: true), EditorCommands.Cut);
        map.Bind(new(ImGuiKey.C, Ctrl: true), EditorCommands.Copy);
        map.Bind(new(ImGuiKey.Insert, Ctrl: true), EditorCommands.Copy);
        map.Bind(new(ImGuiKey.V, Ctrl: true), EditorCommands.Paste);
        map.Bind(new(ImGuiKey.Insert, Shift: true), EditorCommands.Paste);
        map.Bind(new(ImGuiKey.A, Ctrl: true), EditorCommands.SelectAll);
        map.Bind(new(ImGuiKey.Slash, Ctrl: true), EditorCommands.ToggleComment);
        map.Bind(new(ImGuiKey.Space, Ctrl: true), EditorCommands.TriggerCompletion);

        map.Bind(new(ImGuiKey.Equal, Ctrl: true), EditorCommands.ZoomIn);
        map.Bind(new(ImGuiKey.KeypadAdd, Ctrl: true), EditorCommands.ZoomIn);
        map.Bind(new(ImGuiKey.Minus, Ctrl: true), EditorCommands.ZoomOut);
        map.Bind(new(ImGuiKey.KeypadSubtract, Ctrl: true), EditorCommands.ZoomOut);
        map.Bind(new(ImGuiKey._0, Ctrl: true), EditorCommands.ZoomReset);
        return map;
    }
}
