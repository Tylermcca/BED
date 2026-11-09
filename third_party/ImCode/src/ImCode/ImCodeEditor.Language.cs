using Brutal.ImGuiApi;
using Brutal.Numerics;
using ImCode.Languages;
using ImCode.Text;

namespace ImCode;

// hover tooltip, completion popup and diagnostics
public sealed partial class ImCodeEditor
{
    CompletionList? _completion;
    int _completionIndex;
    int _completionFirst;
    int _completionDocumentVersion;
    TextPosition _completionCaret;

    int _diagnosticsVersion = -1;
    double _lastChangeTime;

    float2 _lastMousePos;
    double _mouseMoveTime;
    bool _hoverSuppressed;
    TextPosition? _hoverPos;
    int _hoverVersion = -1;
    bool _showHover;
    readonly List<(string Text, ImColor8? Color)> _hoverLines = [];

    public bool IsCompletionOpen => _completion != null;

    void OnDocumentChanged(TextDocument doc, TextChange change)
    {
        _lastChangeTime = ImGui.GetTime();
        for (int i = 0; i < Diagnostics.Count; i++)
        {
            var d = Diagnostics[i];
            var r = new TextRange(change.Transform(d.Range.Start), change.Transform(d.Range.End));
            if (r != d.Range)
                Diagnostics[i] = d with { Range = r };
        }
    }

    void UpdateDiagnostics()
    {
        if (_diagnosticsVersion == Document.Version || ImGui.GetTime() - _lastChangeTime < Options.DiagnosticsDelay)
            return;
        _diagnosticsVersion = Document.Version;
        var diagnostics = Language.GetDiagnostics(Document);
        if (diagnostics == null)
            return;
        Diagnostics.Clear();
        Diagnostics.AddRange(diagnostics);
        _hoverVersion = -1;
    }

    #region Hover

    void UpdateHover(bool hovered)
    {
        var mouse = ImGui.GetMousePos();
        if (mouse.X != _lastMousePos.X || mouse.Y != _lastMousePos.Y)
        {
            _lastMousePos = mouse;
            _mouseMoveTime = ImGui.GetTime();
            _hoverSuppressed = false;
        }

        _showHover = false;
        if (!hovered || _hoverSuppressed || _completion != null || _dragMode != DragMode.None || ImGui.IsMouseDown(ImGuiMouseButton.Left))
            return;
        if (ImGui.GetTime() - _mouseMoveTime < Options.HoverDelay)
            return;

        var pos = HoveredCharacter(mouse);
        if (pos == null)
            return;
        if (pos != _hoverPos || _hoverVersion != Document.Version)
        {
            _hoverPos = pos;
            _hoverVersion = Document.Version;
            BuildHover(pos.Value);
        }
        _showHover = _hoverLines.Count > 0;
    }

    /// <summary>Position of the character under the mouse, null if the mouse is not over text.</summary>
    TextPosition? HoveredCharacter(float2 mouse)
    {
        int line = (int)MathF.Floor((mouse.Y - _textTop) / LineHeight);
        if (line < 0 || line >= Document.LineCount)
            return null;
        var layout = _layout[line];
        float visual = (mouse.X - _textOrigin.X) / CharWidth;
        if (visual < 0 || visual >= layout.VisualLength)
            return null;
        int col = layout.ColumnFromVisual(visual - 0.5f);
        return col < layout.Source.Length ? new TextPosition(line, col) : null;
    }

    void BuildHover(TextPosition pos)
    {
        _hoverLines.Clear();
        foreach (var d in Diagnostics)
        {
            var r = d.Range.Normalized;
            bool hit = r.IsEmpty ? r.Start == pos : r.Start <= pos && pos < r.End;
            if (hit)
                _hoverLines.Add((d.Message, Theme[d.Severity]));
        }
        var hover = Language.GetHover(Document, pos);
        if (!string.IsNullOrEmpty(hover))
            _hoverLines.Add((hover, null));
    }

    void DrawHover()
    {
        if (!_showHover)
            return;
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 40);
        foreach (var (text, color) in _hoverLines)
        {
            if (color is { } c)
                ImGui.PushStyleColor(ImGuiCol.Text, c);
            ImGui.Text(text);
            if (color != null)
                ImGui.PopStyleColor();
        }
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    #endregion

    #region Completion

    /// <summary>Queries the language for completions at the caret. Opens the popup if open is true.</summary>
    public void UpdateCompletion(bool open = false)
    {
        if (!open && _completion == null)
            return;
        CompletionList? list = null;
        if (!ReadOnly && !Session.HasSelection)
            list = Language.GetCompletions(Document, Session.Caret);
        if (list == null || list.Items.Count == 0)
        {
            CloseCompletion();
            return;
        }
        _completion = list;
        _completionIndex = 0;
        _completionFirst = 0;
        _completionDocumentVersion = Document.Version;
        _completionCaret = Session.Caret;
    }

    public void CloseCompletion() => _completion = null;

    void RefreshCompletion()
    {
        if (_completion == null || (_completionDocumentVersion == Document.Version && _completionCaret == Session.Caret))
            return;
        var caret = Session.Caret;
        if (caret.Line != _completion.Range.Start.Line || caret < _completion.Range.Start)
            CloseCompletion();
        else
            UpdateCompletion();
    }

    bool HandleCompletionKeys()
    {
        if (_completion == null)
            return false;
        int count = _completion.Items.Count;
        int page = Math.Max(1, Options.MaxCompletionItems - 1);

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            CloseCompletion();
        else if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
            _completionIndex = (_completionIndex + 1) % count;
        else if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
            _completionIndex = (_completionIndex - 1 + count) % count;
        else if (ImGui.IsKeyPressed(ImGuiKey.PageDown))
            _completionIndex = Math.Min(count - 1, _completionIndex + page);
        else if (ImGui.IsKeyPressed(ImGuiKey.PageUp))
            _completionIndex = Math.Max(0, _completionIndex - page);
        else if (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter) || ImGui.IsKeyPressed(ImGuiKey.Tab))
            AcceptCompletion();
        else
            return false;
        return true;
    }

    public void AcceptCompletion()
    {
        if (_completion == null)
            return;
        var item = _completion.Items[_completionIndex];
        var range = new TextRange(_completion.Range.Start, TextPosition.Max(_completion.Range.End, Session.Caret));
        CloseCompletion();
        Session.Replace(range, item.Text);
    }

    void DrawCompletion()
    {
        if (_completion == null || !HasFocus)
            return;
        var items = _completion.Items;
        int visible = Math.Min(items.Count, Math.Max(1, Options.MaxCompletionItems));
        if (_completionIndex < _completionFirst)
            _completionFirst = _completionIndex;
        else if (_completionIndex >= _completionFirst + visible)
            _completionFirst = _completionIndex - visible + 1;

        var style = ImGui.GetStyle();
        float rowHeight = ImGui.GetTextLineHeightWithSpacing();
        float height = visible * rowHeight + style.WindowPadding.Y * 2 + (items.Count > visible ? rowHeight : 0);
        var pos = ScreenPosition(_completion.Range.Start) - new float2(style.WindowPadding.X, 0);
        var viewport = ImGui.GetMainViewport();
        if (pos.Y + LineHeight + height > viewport.WorkPos.Y + viewport.WorkSize.Y)
            pos.Y -= height;
        else
            pos.Y += LineHeight;

        ImGui.SetNextWindowPos(pos);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.PopupBackground);
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.PopupSelection);
        ImGui.BeginTooltip();
        for (int i = _completionFirst; i < _completionFirst + visible; i++)
        {
            var item = items[i];
            ImGui.PushStyleColor(ImGuiCol.Text, Theme[item.Kind]);
            ImGui.Selectable(item.Label, i == _completionIndex);
            ImGui.PopStyleColor();
            if (item.Detail != null)
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.PopupDetail);
                ImGui.Text(item.Detail);
                ImGui.PopStyleColor();
            }
        }
        if (items.Count > visible)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.PopupDetail);
            ImGui.Text($"{_completionIndex + 1} / {items.Count}");
            ImGui.PopStyleColor();
        }
        ImGui.EndTooltip();
        ImGui.PopStyleColor(2);
    }

    #endregion

    void DrawPopups()
    {
        DrawCompletion();
        DrawHover();
    }
}
