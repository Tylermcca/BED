using Bed;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Checkers;

namespace CheckersPlay;


/// <summary>
/// play-checkers: the game in its own window. A match fills it: click a piece, then a square; "Computer move" lets the
/// computer play; "Take back" and "New game". It starts from a position file (one the editor saved, or the editor's Play
/// in its own window), or the standard opening.
/// <code>
/// play-checkers [position.json] [--shot file.png]
/// </code>
/// </summary>
public static class Program
{
    private const string Usage = """
        usage: play-checkers [position.json] [--shot <file.png>]
          with no position, the standard opening. --shot draws the game into a PNG instead of opening a window.

        BRUTAL game development framework is Copyright (c) 2024 Ahwoo Limited. See NOTICE.txt.
        """;

    /// <summary>The entry point. Exit codes: 0 played; 1 the position can't be read (with why); 2 bad arguments.</summary>
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Runs the game.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter errors)
    {
        string? file = null, shot = null;
        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--shot" when i + 1 < args.Length: shot = args[++i]; break;
                case "-h" or "--help": output.Write(Usage); return 0;
                case var a when !a.StartsWith('-') && file is null: file = a; break;
                default: errors.Write(Usage); return 2;
            }
        Game start;
        try { start = file is null ? Game.Opening() : Position.Read(File.ReadAllText(file)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DocumentFormatException or InvalidDocumentException or System.Text.Json.JsonException)   // an empty or broken file too (found by A071)
        {
            errors.WriteLine($"{file} isn't a checkers position: {e.Message}");
            return 1;
        }
        using var play = new Play(start, title: file is null);
        if (shot is not null)
        {
            using var host = new HeadlessHost(720, 800);
            host.Frames(play.Draw, 3);
            host.Save(shot);
            output.WriteLine($"Drew the game into {Path.GetFullPath(shot)}.");
            return 0;
        }
        var splash = new BrutalSplash();   // the BRUTAL logo before the game (BRUTAL License v1.0, 2.2)
        EditorApp.Run(new("Checkers", 720, 800), _ => { }, () => { if (!splash.Draw()) play.Draw(); });
        return 0;
    }
}

/// <summary>The game: a match fills the window, with New game to start over from the same position. With its Gum screens
/// beside it (CheckersProject/ui/CheckersUi.gumj, made in Gum), a title comes first when <paramref name="title"/> is true, and a strip above
/// the board shows whose turn it is and the pieces left; without them, or with <paramref name="plain"/>, a plain line does.</summary>
public sealed class Play(Game start, bool title = false, bool plain = false) : IDisposable
{
    private readonly string? gum = plain ? null : CheckersScreen.Project();
    private CheckersScreen? titleScreen, panel;
    private bool onTitle = title;

    /// <summary>The match being played.</summary>
    public Match Match { get; private set; } = new(start);

    /// <summary>The Gum strip above the board, once a match has been drawn with the Gum screens.</summary>
    public CheckersScreen? Strip => panel;

    /// <summary>The Gum title, once drawn.</summary>
    public CheckersScreen? TitleScreen => titleScreen;

    /// <summary>Whether the title is showing (only with the Gum screens).</summary>
    public bool OnTitle => onTitle && gum is not null;

    /// <summary>Draws a frame.</summary>
    public void Draw()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        if (ImGui.Begin("Checkers###game"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings))
        {
            // The credits line (BRUTAL License v1.0, 2.1), wrapped so it's whole at any text size, with room kept for it
            // under the board or the title (found by A067: at 1.5x and 2x the board pushed it out of the window).
            var credit = ImGui.CalcTextSize(BrutalCredit.Copyright, false, ImGui.GetContentRegionAvail().X).Y + ImGui.GetStyle().ItemSpacing.Y * 2;
            if (OnTitle)
            {
                titleScreen ??= new CheckersScreen(gum!, "Title", fill: true);
                if (titleScreen.Draw(credit) == "NewGameButton") { onTitle = false; Match = new Match(start); }
            }
            else
            {
                if (gum is not null)
                {
                    panel ??= new CheckersScreen(gum, "MatchPanel", fill: false);
                    var (dark, light) = Rules.Count(Match.Current);
                    panel.Set("TurnText.Text", Match.Status);
                    panel.Set("CountText.Text", $"Dark {dark}   Light {light}");
                    if (panel.Draw() == "NewGameButton") Match = new Match(start);
                }
                else
                {
                    if (ImGui.Button("New game"u8)) Match = new Match(start);
                    ImGui.SameLine();
                    ImGui.TextDisabled(Match.Status);
                }
                Match.ShowsStatus = gum is null;   // the Gum strip already says whose move it is and the pieces left
                Match.Draw(credit);
            }
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyleColorVec4(ImGuiCol.TextDisabled));
            ImGui.TextWrapped(BrutalCredit.Copyright);
            ImGui.PopStyleColor();
        }
        ImGui.End();
    }

    /// <summary>Lets go of the Gum screens.</summary>
    public void Dispose()
    {
        titleScreen?.Dispose();
        panel?.Dispose();
    }
}
