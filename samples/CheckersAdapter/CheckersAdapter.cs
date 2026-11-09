using System.Diagnostics;
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Checkers;

namespace CheckersAdapter;

/// <summary>
/// Checkers in the general app: a position is a data file (<c>"kind": "checkers.game"</c>), checked by its schema and by
/// the game's own rules, so a board the game would refuse can't be saved. Each position gets a Board panel (click a dark
/// square to change what stands on it), a Match play mode to play inside the editor while making it, and Play in its own
/// window, which starts the game (play-checkers) on the position being edited. BORROWED: Unity's and Unreal's play inside
/// the editor, and Godot's game in its own window; the game's code is the same in both.
/// </summary>
public sealed class CheckersAdapter : IProjectAdapter
{
    /// <summary>Starts a process; replaced in tests, so no window opens.</summary>
    public static Func<ProcessStartInfo, bool> Launch { get; set; } = info => Process.Start(info) is not null;

    /// <inheritdoc />
    public string Name => "Checkers";

    /// <inheritdoc />
    public bool Recognises(ProjectPaths project) =>
        Directory.Exists(project.Data) && Directory.EnumerateFiles(project.Data, "*.json").Any(f => IsPosition(File.ReadAllText(f)));

    /// <inheritdoc />
    public DataProject Open(ProjectPaths project) => DataProject.Open(project.Data, project.Schemas, rules: _ => new GameRules());

    /// <inheritdoc />
    public void Configure(EditorModel<DataDocument> editor, DataTableView table)
    {
        if (!IsPosition(editor.Session.Current.Root)) return;
        var board = new BoardView(editor);
        editor.Add(new Panel("checkers.board", "Board", board.Draw, Width: 480, Height: 600));
        editor.Add(new PlayMode<DataDocument>("checkers.match", "Match", doc => new Match(Position.Read(doc.Root.GetRawText()))));
    }

    /// <summary>Whether <paramref name="root"/> is a checkers position.</summary>
    public static bool IsPosition(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("kind"u8, out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "checkers.game";

    private static bool IsPosition(string text)
    {
        try { using var doc = JsonDocument.Parse(text); return IsPosition(doc.RootElement); }
        catch (JsonException) { return false; }
    }

    /// <summary>Where the game is: beside this adapter, beside the app, or in the checkers project itself (a release keeps
    /// it in samples\checkers). Null when it isn't there.</summary>
    public static ProcessStartInfo? GameFor(string position)
    {
        var here = Path.GetDirectoryName(typeof(CheckersAdapter).Assembly.Location) ?? AppContext.BaseDirectory;
        var data = Path.GetDirectoryName(Path.GetFullPath(position));
        var project = data is null ? null : Path.GetDirectoryName(data);
        foreach (var dir in new[] { here, AppContext.BaseDirectory, project }.OfType<string>())
        {
            if (File.Exists(Path.Combine(dir, "play-checkers.exe")))
                return new ProcessStartInfo(Path.Combine(dir, "play-checkers.exe")) { ArgumentList = { position }, UseShellExecute = false };
            if (File.Exists(Path.Combine(dir, "play-checkers.dll")))
                return new ProcessStartInfo("dotnet") { ArgumentList = { Path.Combine(dir, "play-checkers.dll"), position }, UseShellExecute = false };
        }
        return null;
    }

    // The game's own rules on a position file, so the editor refuses what the game would (a man on its crowning row, a
    // piece on a light square), with the game's message. Files that aren't positions are left to their schemas.
    private sealed class GameRules : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument document)
        {
            if (!IsPosition(document.Root)) return [];
            try
            {
                Position.Read(document.Root.GetRawText());
                return [];
            }
            catch (DocumentFormatException e) { return [Issue.Error(e.Message)]; }
            catch (InvalidDocumentException e) { return e.Issues; }
        }
    }
}

/// <summary>The Board panel: the position drawn as the game draws it; a click on a dark square changes what stands there
/// (empty, dark man, dark king, light man, light king, and round again) as one undoable edit; whose turn it is; and
/// Play in its own window.</summary>
public sealed class BoardView(EditorModel<DataDocument> editor)
{
    private const string Order = ".dDlL";

    /// <summary>What the panel last said.</summary>
    public string Said { get; private set; } = "Click a dark square to change what stands on it.";

    /// <summary>Where the board was last drawn, so a test can click a square by name.</summary>
    public (Brutal.Numerics.float2 Origin, float Cell) LastBoard { get; private set; }

    /// <summary>The position being edited, or null when the file doesn't hold a valid one.</summary>
    public Game? Current
    {
        get
        {
            try { return Position.Read(editor.Session.Current.Root.GetRawText()); }
            catch (Exception e) when (e is DocumentFormatException or InvalidDocumentException) { return null; }
        }
    }

    /// <summary>Changes what stands on <paramref name="square"/> to the next piece in turn. Returns false, saying why,
    /// when the game's rules refuse it (a man on the row where it would be crowned).</summary>
    public bool Cycle(string square)
    {
        if (Current is not { } g || g.Parse(square) is not { } at || !Game.Dark(g.Size, at.R, at.C)) { Said = $"{square} isn't a dark square."; return false; }
        var piece = g.At(at.R, at.C);
        var next = Order[(Order.IndexOf(piece) + 1) % Order.Length];
        var row = g.Rows[at.R].ToCharArray();
        row[at.C] = next;
        var ok = editor.Execute(new SetValue($"/board/{at.R}", JsonSerializer.SerializeToElement(new string(row))));
        // A man can't stand where it would be crowned; step on to the next piece, so a click never leaves the square stuck.
        if (!ok && next is 'd' or 'l')
        {
            row[at.C] = Order[(Order.IndexOf(next) + 1) % Order.Length];
            ok = editor.Execute(new SetValue($"/board/{at.R}", JsonSerializer.SerializeToElement(new string(row))));
            next = row[at.C];
        }
        Said = ok ? $"{square}: {Game.Describe(next)}." : string.Join(" ", editor.Session.LastIssues.Select(i => i.Message));
        return ok;
    }

    /// <summary>Gives the turn to the other side, as one undoable edit.</summary>
    public bool SwitchTurn()
    {
        if (Current is not { } g) return false;
        var side = g.ToMove == Side.Dark ? "light" : "dark";
        var ok = editor.Execute(new SetValue("/to_move", JsonSerializer.SerializeToElement(side)));
        Said = ok ? $"{side} to move." : string.Join(" ", editor.Session.LastIssues.Select(i => i.Message));
        return ok;
    }

    /// <summary>Starts the game in its own window on the position being edited, unsaved edits included (from a copy, so
    /// the game never writes the file). Returns false, saying why, when the position isn't valid or the game isn't found.</summary>
    public bool PlayInOwnWindow()
    {
        if (Current is null) { Said = "This position isn't valid yet, so it can't be played."; return false; }
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "brutal-checkers")).FullName;
        var copy = Path.Combine(folder, $"position-{Guid.NewGuid():N}.json");   // its own copy per launch, so a later one can't change it (found by A071)
        File.WriteAllText(copy, new DataFormat().Write(editor.Session.Current));
        if (CheckersAdapter.GameFor(copy) is not { } start) { Said = "The game (play-checkers) isn't beside the adapter or the app."; return false; }
        var ok = CheckersAdapter.Launch(start);
        Said = ok ? "Started the game in its own window." : "The game didn't start.";
        return ok;
    }

    /// <summary>Draws the panel.</summary>
    public void Draw()
    {
        if (Current is not { } g)
        {
            ImGui.TextWrapped("This file doesn't hold a valid position yet; the table and the issues list say why."u8);
            return;
        }
        if (ImGui.Button("Play in its own window"u8)) PlayInOwnWindow();
        ImGui.SameLine();
        if (ImGui.Button((g.ToMove == Side.Dark ? "Dark" : "Light") + " to move: switch")) SwitchTurn();
        ImGui.TextDisabled(Said);
        LastBoard = Board.Draw(g, [], square => Cycle(square));
    }
}
