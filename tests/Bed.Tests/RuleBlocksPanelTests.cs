using System.Globalization;
using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

/// <summary>BED-BLOCKS01: the generic rule-blocks editor. A tiny adapter (a "When the game starts" stack of Say and Repeat blocks
/// and one big block) supplies the model; BED draws it, keeps every change as one undoable edit of the open file, saves it,
/// checks it off the window's thread with findings by block, and hands Play the adapter's arguments. INVENTED fixtures.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class RuleBlocksPanelTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("ruleblocks-").FullName;
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");

    private const string Game = """
        {"kind":"tiny_blocks","name":"Tiny game","bigs":{"wave":"linked"},"stacks":[{"when":"start","do":[
          {"block":"say","words":"Hello","times":2,"to":["Ann","Bo"]},
          {"block":"repeat","times":3,"do":[{"block":"say","words":"Again","times":1}]},
          {"block":"big","big":"wave"}]}]}
        """;

    public RuleBlocksPanelTests()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Schemas);
        File.WriteAllText(Path.Combine(Schemas, "game.schema.json"), """{"type":"object"}""");
        File.WriteAllText(Path.Combine(Schemas, "notes.schema.json"), """{"type":"object","properties":{"notes":{"type":"array","items":{"type":"string"}}}}""");
        File.WriteAllText(Path.Combine(Data, "game.json"), Game);
        File.WriteAllText(Path.Combine(Data, "notes.json"), """{"notes":["a"]}""");
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
    }

    // ------------------------------------------------------------------ the tiny adapter

    private sealed class TinyAdapter : IProjectAdapter
    {
        public readonly TinyBlocks Blocks = new();
        public string Name => "Tiny blocks";
        public DataProject Open(ProjectPaths project) => DataProject.Open(project.Data, project.Schemas);
        public void Configure(EditorModel<DataDocument> editor, DataTableView table) { }
        public IRuleBlocks? RuleBlocks(ProjectPaths project) => Blocks;
    }

    private sealed class TinyBlocks : IRuleBlocks
    {
        public int Checks;
        public bool Handles(string fileName, string text) => text.Contains("\"tiny_blocks\"", StringComparison.Ordinal);
        public IReadOnlyList<RuleBlockGroup> Groups { get; } = [new("talk", "Talking", 0x3A7BD5), new("steps", "Steps and repeats", 0xE08A1E), new("big", "Big blocks", 0x8E44AD)];
        public IReadOnlyList<RuleBlockPaletteItem> Palette { get; } = [new("say", "Say something", "talk"), new("repeat", "Repeat", "steps"), new("big", "Wave", "big")];
        public IRuleBlockDocument Open(string text) => new TinyDocument(text);

        /// <summary>When set, a passing Check gives back the file with a record of it written in ("checked": the times it ran).</summary>
        public bool Stamps;
        /// <summary>When set, Check waits for it before giving its result (so a test can change the file meanwhile).</summary>
        public ManualResetEventSlim? Hold;

        public RuleBlockCheckResult Check(string text, CancellationToken cancel)
        {
            var n = Interlocked.Increment(ref Checks);
            Hold?.Wait(TimeSpan.FromSeconds(30), cancel);
            var doc = new TinyDocument(text);
            var empty = doc.Rows().Where(r => r.Title == "Say" && doc.Node(r)!["words"]!.GetValue<string>().Length == 0).ToList();
            string? Stamped() { if (!Stamps) return null; var file = JsonNode.Parse(text)!.AsObject(); file["checked"] = n; return file.ToJsonString(); }
            return empty.Count == 0 ? new(true, "It can be played.", [], Stamped())
                : new(false, $"{empty.Count} block(s) say nothing.", [.. empty.Select(r => new RuleBlockFinding(r.Id, "This block says nothing.")), new RuleBlockFinding(null, "Fix the empty words first.")]);
        }

        public IReadOnlyList<string>? PlayArguments(IReadOnlyList<string> current, string fileName, string text, out string why)
        {
            why = "";
            return [.. current.Where(a => a != "--rules"), "--rules", fileName];
        }
    }

    private sealed class TinyDocument(string text) : IRuleBlockDocument
    {
        private JsonObject root = JsonNode.Parse(text)!.AsObject();
        private readonly HashSet<string> open = [];
        public string Text => root.ToJsonString();
        public string Name => root["name"]!.GetValue<string>();
        public string? Problem { get; private set; }
        public string Done { get; private set; } = "";

        public JsonObject? Node(RuleBlockRow row) => At(root, row.Id) as JsonObject;

        private static JsonNode? At(JsonNode from, string path)
        {
            JsonNode? n = from;
            foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
                n = n is JsonArray a && int.TryParse(part, out var i) && i < a.Count ? a[i] : n is JsonObject o ? o[part] : null;
            return n;
        }

        public IReadOnlyList<RuleBlockRow> Rows()
        {
            var rows = new List<RuleBlockRow>();
            var stacks = root["stacks"]!.AsArray();
            for (var s = 0; s < stacks.Count; s++)
            {
                rows.Add(new($"/stacks/{s}", 0, RuleBlockKind.Hat, "When the game starts", "steps", RuleBlockShape.Hat));
                Body(stacks[s]!["do"]!.AsArray(), $"/stacks/{s}/do", 1, rows);
            }
            return rows;
        }

        private void Body(JsonArray list, string at, int depth, List<RuleBlockRow> rows)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var b = list[i]!.AsObject(); var here = $"{at}/{i}";
                switch (b["block"]!.GetValue<string>())
                {
                    case "say": rows.Add(new(here, depth, RuleBlockKind.Block, "Say", "talk")); break;
                    case "repeat":
                        rows.Add(new(here, depth, RuleBlockKind.Block, "Repeat", "steps", RuleBlockShape.Wrap));
                        Body(b["do"]!.AsArray(), here + "/do", depth + 1, rows); break;
                    default:
                        var state = root["bigs"]![b["big"]!.GetValue<string>()]!.GetValue<string>() == "own" ? BigBlockState.Own : BigBlockState.Linked;
                        rows.Add(new(here, depth, RuleBlockKind.Block, "Wave", "big", Big: state, Open: open.Contains(here), CanGoBack: true));
                        if (open.Contains(here)) rows.Add(new(here + "/inner", depth + 1, RuleBlockKind.Block, "Say", "talk"));
                        break;
                }
            }
        }

        public IReadOnlyList<RuleBlockSlot> Slots(RuleBlockRow row) => Node(row) is { } b && b["block"]?.GetValue<string>() is "say" or "repeat"
            ? b["block"]!.GetValue<string>() == "say"
                ? [new("words", "Words", RuleBlockSlotKind.Words, b["words"]!.GetValue<string>(), []), new("times", "Times", RuleBlockSlotKind.Number, b["times"]!.ToJsonString(), [], 1, 5),
                   .. b["to"] is JsonArray to ? new RuleBlockSlot[] { new("to", "Says it to", RuleBlockSlotKind.List, string.Join(", ", to.Select(n => n!.GetValue<string>())),
                       [new("Ann", "Ann"), new("Bo", "Bo"), new("Cy", "Cy")], Items: [.. to.Select(n => n!.GetValue<string>())]) } : []]
                : [new("times", "Times", RuleBlockSlotKind.Number, b["times"]!.ToJsonString(), [], 1, 9)]
            : [];

        // Say's sentence weaves its slots in; Repeat keeps the plain form (the title, then each slot with its label).
        public IReadOnlyList<RuleBlockPart> Sentence(RuleBlockRow row) => Node(row) is { } b && b["block"]?.GetValue<string>() == "say"
            ? [new("Say "), new("", "words"), new(" "), new("", "times"), new(" times")]
            : [new(row.Title), .. Slots(row).SelectMany(s => new RuleBlockPart[] { new(" " + s.Label.ToLowerInvariant() + " "), new("", s.Key) })];

        private bool List(RuleBlockRow row, string key, Func<JsonArray, string?> edit, string done) => Change(r =>
            At(r, row.Id) is JsonObject b && b[key] is JsonArray list ? edit(list) : "That list isn't there any more.", done);
        public bool AddItem(RuleBlockRow row, string key, string value) => List(row, key, l => { l.Add(value); return null; }, "Added it.");
        public bool RemoveItem(RuleBlockRow row, string key, int index) => List(row, key, l => { l.RemoveAt(index); return null; }, "Took it out.");
        public bool MoveItem(RuleBlockRow row, string key, int index, int by) => List(row, key, l =>
        {
            var j = index + by; if (j < 0 || j >= l.Count) return "It can't go further.";
            var n = l[index]!; l.RemoveAt(index); l.Insert(j, n); return null;
        }, "Moved it.");

        public string Says(RuleBlockRow row) => Node(row) is { } b && b["block"]?.GetValue<string>() == "say"
            ? $"Say \"{b["words"]}\" {b["times"]} times." : row.Title + ".";

        private bool Change(Func<JsonObject, string?> edit, string done)
        {
            var copy = root.DeepClone().AsObject();
            if (edit(copy) is { } why) { Problem = why; return false; }
            root = copy; Problem = null; Done = done; return true;
        }

        public bool Set(RuleBlockRow row, string key, string value) => Change(r =>
        {
            if (At(r, row.Id) is not JsonObject b) return "That block isn't there any more.";
            b[key] = key == "words" ? JsonValue.Create(value) : JsonValue.Create(int.Parse(value, CultureInfo.InvariantCulture));
            return null;
        }, "Changed it.");

        public bool Step(RuleBlockRow row, RuleBlockSlot slot, int by)
        {
            var next = int.Parse(slot.Shown, CultureInfo.InvariantCulture) + by;
            if (next < slot.Min || next > slot.Max) { Problem = by > 0 ? "That's the most it can be." : "That's the least it can be."; return false; }
            return Set(row, slot.Key, next.ToString(CultureInfo.InvariantCulture));
        }

        private static (JsonArray List, int Index) Place(JsonObject r, string id)
        {
            var cut = id.LastIndexOf('/');
            return (At(r, id[..cut])!.AsArray(), int.Parse(id[(cut + 1)..], CultureInfo.InvariantCulture));
        }

        public bool Add(string paletteKey, RuleBlockRow? at) => Change(r =>
        {
            JsonObject made = paletteKey switch
            {
                "say" => new() { ["block"] = "say", ["words"] = "Hi", ["times"] = 1 },
                "repeat" => new() { ["block"] = "repeat", ["times"] = 2, ["do"] = new JsonArray() },
                _ => new() { ["block"] = "big", ["big"] = "wave" },
            };
            if (at is null || at.Kind == RuleBlockKind.Hat) { At(r, (at?.Id ?? "/stacks/0") + "/do")!.AsArray().Insert(0, made); return null; }
            var (list, index) = Place(r, at.Id); list.Insert(index + 1, made); return null;
        }, "Added it.");

        public bool Move(RuleBlockRow row, int by) => Change(r =>
        {
            if (row.Kind != RuleBlockKind.Block) return "This can't move.";
            var (list, i) = Place(r, row.Id); var j = i + by;
            if (j < 0 || j >= list.Count) return by < 0 ? "It's already at the top." : "It's already at the bottom.";
            var node = list[i]!; list.RemoveAt(i); list.Insert(j, node); return null;
        }, "Moved it.");

        public bool MoveTo(RuleBlockRow row, RuleBlockRow target) => Change(r =>
        {
            if (target.Id.StartsWith(row.Id + "/", StringComparison.Ordinal)) return "A block can't go inside itself.";
            var (from, i) = Place(r, row.Id); var node = from[i]!;
            var marker = new JsonObject { ["block"] = "say", ["words"] = "", ["times"] = 1 };
            if (target.Kind == RuleBlockKind.Hat) At(r, target.Id + "/do")!.AsArray().Insert(0, marker);
            else { var (to, k) = Place(r, target.Id); to.Insert(k + 1, marker); }
            from.Remove(node);
            var parent = (JsonArray)marker.Parent!; parent[parent.IndexOf(marker)] = node;
            return null;
        }, "Moved the block.");

        public bool Delete(RuleBlockRow row) => Change(r =>
        {
            if (row.Kind != RuleBlockKind.Block) return "This can't be taken out.";
            var (list, i) = Place(r, row.Id); list.RemoveAt(i); return null;
        }, "Took it out.");

        public bool SetOpen(RuleBlockRow row, bool opened) { if (opened) open.Add(row.Id); else open.Remove(row.Id); return true; }
        public bool MakeItMine(RuleBlockRow row) => Change(r => { r["bigs"]!["wave"] = "own"; return null; }, "It's yours now.");
        public bool GoBack(RuleBlockRow row) => Change(r => { r["bigs"]!["wave"] = "linked"; return null; }, "Back to the standard one.");
        public void Reload(string text) { root = JsonNode.Parse(text)!.AsObject(); Problem = null; }
    }

    // ------------------------------------------------------------------ helpers

    private (ProjectLauncher Launcher, DataWorkspace Workspace, RuleBlocksPanel Panel, TinyAdapter Adapter) Open()
    {
        var adapter = new TinyAdapter();
        var launcher = new ProjectLauncher(new RecentProjects(Path.Combine(root, "settings", "recent.json"))) { Adapter = adapter };
        Assert.True(launcher.Open(new ProjectPaths(Data, Schemas)), launcher.Said);
        var w = launcher.Workspace!;
        Assert.True(w.Show("game.json"), w.Said);
        var panel = w.RuleBlocksOf("game.json");
        Assert.NotNull(panel);
        return (launcher, w, panel!, adapter);
    }

    private static string Text(DataWorkspace w) => new DataFormat().Write(w.Window!.Editor.Session.Current);

    private static RuleBlockRow RowTitled(RuleBlocksPanel panel, string title, int nth = 0) => panel.Rows.Where(r => r.Title == title).ElementAt(nth);

    // ------------------------------------------------------------------ tests

    [Fact]
    public void Only_the_files_the_adapter_names_get_the_block_editor()
    {
        var (launcher, w, panel, _) = Open();
        using var _l = launcher;
        Assert.Contains(w.Window!.Editor.Panels, p => p.Id == RuleBlocksPanel.Id);
        Assert.Equal("Tiny game", panel.Document!.Name);
        Assert.Equal(["When the game starts", "Say", "Repeat", "Say", "Wave"], panel.Rows.Select(r => r.Title));
        Assert.True(w.Show("notes.json"), w.Said);
        Assert.DoesNotContain(w.Window!.Editor.Panels, p => p.Id == RuleBlocksPanel.Id);
        Assert.Null(w.RuleBlocksOf("notes.json"));
    }

    [Fact]
    public void Each_change_is_one_undoable_edit_of_the_file_and_saves_like_any_other()
    {
        var (launcher, w, panel, _) = Open();
        using var _l = launcher;
        var session = w.Window!.Editor.Session;
        var say = RowTitled(panel, "Say");
        Assert.True(panel.Step(say.Id, "times", 1), panel.Said);
        Assert.Contains("\"times\":3", Text(w).Replace(" ", ""));
        Assert.True(panel.Set(say.Id, "words", "Good day"), panel.Said);
        Assert.True(panel.Select(RowTitled(panel, "Say").Id));
        Assert.True(panel.Add("repeat"), panel.Said);
        Assert.Equal("Repeat", panel.SelectedRow!.Title);   // what was added is chosen
        Assert.Equal(2, panel.Rows.Count(r => r.Title == "Repeat"));
        Assert.True(panel.MoveSelected(1), panel.Said);
        Assert.Equal("Repeat", panel.SelectedRow!.Title);   // the moved block stays chosen
        Assert.True(panel.DeleteSelected(), panel.Said);
        Assert.Single(panel.Rows, r => r.Title == "Repeat");
        Assert.True(session.Dirty);

        // A refused change says why and leaves the file as it was.
        var before = session.Current;
        var nested = RowTitled(panel, "Say", 1);
        Assert.False(panel.Step(RowTitled(panel, "Repeat").Id, "times", 7));
        Assert.Equal("That's the most it can be.", panel.Said);
        Assert.Same(before, session.Current);

        // BED's own undo and redo walk the changes back, and the blocks follow.
        Assert.True(panel.Undo());   // the take-out
        Assert.Equal(2, panel.Rows.Count(r => r.Title == "Repeat"));
        Assert.True(w.Window.Editor.Session.Undo());   // the move, undone from outside the panel
        Assert.True(panel.Undo());   // the add
        Assert.Single(panel.Rows, r => r.Title == "Repeat");
        Assert.True(panel.Redo());
        Assert.Equal(2, panel.Rows.Count(r => r.Title == "Repeat"));

        Assert.True(w.Window.Run("editor.save"), w.Window.Status);
        var saved = File.ReadAllText(Path.Combine(Data, "game.json"));
        Assert.Contains("Good day", saved);
        Assert.False(session.Dirty);
        _ = nested;
    }

    [Fact]
    public void Drag_and_drop_moves_a_block_and_big_blocks_can_be_made_mine_and_put_back()
    {
        var (launcher, w, panel, _) = Open();
        using var _l = launcher;
        var first = RowTitled(panel, "Say");
        var wave = RowTitled(panel, "Wave");
        Assert.True(panel.MoveTo(first.Id, wave.Id), panel.Said);
        Assert.Equal(["When the game starts", "Repeat", "Say", "Wave", "Say"], panel.Rows.Select(r => r.Title));

        wave = RowTitled(panel, "Wave");
        Assert.Equal(BigBlockState.Linked, wave.Big);
        Assert.True(panel.ToggleOpen(wave.Id));
        Assert.True(RowTitled(panel, "Wave").Open);
        var revision = w.Window!.Editor.Session.Revision;
        Assert.True(panel.ToggleOpen(wave.Id));   // opening and closing is view only
        Assert.Equal(revision, w.Window.Editor.Session.Revision);
        Assert.True(panel.Select(wave.Id));
        Assert.True(panel.MakeSelectedMine(), panel.Said);
        Assert.Equal(BigBlockState.Own, RowTitled(panel, "Wave").Big);
        Assert.True(panel.GoBackSelected(), panel.Said);
        Assert.Equal(BigBlockState.Linked, RowTitled(panel, "Wave").Big);
        Assert.True(panel.Undo());
        Assert.Equal(BigBlockState.Own, RowTitled(panel, "Wave").Big);
    }

    [Fact]
    public void Check_runs_off_the_window_thread_and_puts_findings_on_their_blocks()
    {
        var (launcher, w, panel, adapter) = Open();
        using var _l = launcher;
        var say = RowTitled(panel, "Say");
        Assert.True(panel.Set(say.Id, "words", ""), panel.Said);
        Assert.True(panel.StartCheck(), panel.Said);
        Assert.True(panel.WaitForCheck(TimeSpan.FromSeconds(30)));
        Assert.Equal(1, adapter.Blocks.Checks);
        var result = panel.Checked!;
        Assert.False(result.Passed);
        Assert.Contains(result.Findings, f => f.RowId == say.Id);
        Assert.Contains(result.Findings, f => f.RowId is null);
        Assert.True(panel.CheckedIsCurrent);
        Assert.True(panel.Set(say.Id, "words", "Hi"));
        Assert.False(panel.CheckedIsCurrent);   // the game changed since: Check's result is old
        using var host = new HeadlessHost(1280, 800);
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        Assert.True(panel.Undo());
        host.Frames(() => DrawPanel(panel), 3);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Label == "Check: This block says nothing.");
    }

    [Fact]
    public void A_passing_Checks_record_is_kept_in_the_file_as_one_undoable_edit()
    {
        var (launcher, w, panel, adapter) = Open();
        using var _l = launcher;
        adapter.Blocks.Stamps = true;
        var before = Text(w);
        Assert.True(panel.StartCheck(), panel.Said);
        Assert.True(panel.WaitForCheck(TimeSpan.FromSeconds(30)));
        Assert.True(panel.Checked!.Passed);
        Assert.Contains("\"checked\"", Text(w), StringComparison.Ordinal);
        Assert.True(panel.CheckedIsCurrent);   // the result is for the file as it now stands, record and all
        Assert.True(panel.Undo());
        Assert.Equal(before, Text(w));   // one Undo takes the record back, and only it
    }

    [Fact]
    public void Checks_record_is_not_kept_over_a_change_made_while_it_ran()
    {
        var (launcher, w, panel, adapter) = Open();
        using var _l = launcher;
        using var hold = new ManualResetEventSlim(false);
        (adapter.Blocks.Stamps, adapter.Blocks.Hold) = (true, hold);
        Assert.True(panel.StartCheck(), panel.Said);
        Assert.True(panel.Set(RowTitled(panel, "Say").Id, "words", "Hi"), panel.Said);
        hold.Set();
        Assert.True(panel.WaitForCheck(TimeSpan.FromSeconds(30)));
        Assert.DoesNotContain("\"checked\"", Text(w), StringComparison.Ordinal);
        Assert.False(panel.CheckedIsCurrent);
    }

    [Fact]
    public void Play_uses_the_projects_one_Play_with_the_adapters_arguments()
    {
        var (launcher, w, panel, _) = Open();
        using var _l = launcher;
        if (w.PlaySettings is { } play)
        {
            play.Reload();
            File.WriteAllText(Path.Combine(root, "game.exe"), "");
            play.SetDraft(new PlaySettings("game.exe", ["--fast"]));
            Assert.True(play.Save(), play.Status);
            panel.Play();
            play.Reload();
            Assert.Equal(["--fast", "--rules", "game.json"], play.Settings!.Arguments);
        }
        else
        {
            Assert.False(panel.Play());
            Assert.Equal("Play isn't available in this window.", panel.Said);
        }
    }

    [Fact]
    public void Slots_sit_in_the_sentence_and_lists_can_be_added_to_taken_from_and_reordered()
    {
        var (launcher, w, panel, _) = Open();
        using var _l = launcher;
        using var host = new HeadlessHost(1280, 800);
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(() => DrawPanel(panel), 3);
        var nodes = Ui.Snapshot().Nodes;
        // "Say [Hello] [- 2 +] times, says it to [Ann, Bo]": the words slot comes after the row's handle, the number after the words,
        // and the list (left out of the sentence) after the number, all on one line.
        var handle = nodes.First(n => n.Label == "Say" && n.Role == UiRole.ListItem);
        var wordsSlot = nodes.First(n => n.Label == "Words");
        var less = nodes.First(n => n.Label.StartsWith("Less times", StringComparison.Ordinal));
        var list = nodes.First(n => n.Label == "Says it to");
        Assert.True(handle.Bounds.X < wordsSlot.Bounds.X && wordsSlot.Bounds.X < less.Bounds.X && less.Bounds.X < list.Bounds.X);
        Assert.True(Math.Abs(wordsSlot.Bounds.Y - less.Bounds.Y) < 8 && Math.Abs(less.Bounds.Y - list.Bounds.Y) < 8);

        var say = RowTitled(panel, "Say");
        string[] Items() => [.. panel.Document!.Slots(RowTitled(panel, "Say")).First(s => s.Key == "to").Items!];
        Assert.True(panel.AddItem(say.Id, "to", "Cy"), panel.Said);
        Assert.Equal(["Ann", "Bo", "Cy"], Items());
        Assert.True(panel.MoveItem(say.Id, "to", 2, -1), panel.Said);
        Assert.Equal(["Ann", "Cy", "Bo"], Items());
        Assert.False(panel.MoveItem(say.Id, "to", 0, -1));
        Assert.Equal("It can't go further.", panel.Said);
        Assert.True(panel.Undo());
        Assert.Equal(["Ann", "Bo", "Cy"], Items());

        // From the list's drop-down: take Ann out.
        host.Frames(() => DrawPanel(panel), 2);
        list = Ui.Snapshot().Nodes.First(n => n.Label == "Says it to");
        host.Click(() => DrawPanel(panel), list.Bounds.X + 6, list.Bounds.Y + 6);
        host.Frames(() => DrawPanel(panel), 2);
        var takeOut = Ui.Snapshot().Nodes.First(n => n.Label == "Take Ann out");
        host.Click(() => DrawPanel(panel), takeOut.Bounds.X + 4, takeOut.Bounds.Y + 4);
        Assert.Equal(["Bo", "Cy"], Items());
        Assert.Contains("\"to\":[\"Bo\",\"Cy\"]", Text(w).Replace(" ", ""));
    }

    [Fact]
    public void A_document_that_weaves_nothing_still_shows_every_slot_after_its_title()
    {
        IRuleBlockDocument plain = new PlainDocument();
        var row = new RuleBlockRow("/0", 0, RuleBlockKind.Block, "Deal", "cards");
        Assert.Equal([new("Deal"), new(" how many "), new("", "count")], plain.Sentence(row));
        Assert.False(plain.AddItem(row, "count", "1"));
    }

    private sealed class PlainDocument : IRuleBlockDocument
    {
        public string Text => "{}";
        public string Name => "Plain";
        public string? Problem => null;
        public string Done => "";
        public IReadOnlyList<RuleBlockRow> Rows() => [];
        public IReadOnlyList<RuleBlockSlot> Slots(RuleBlockRow row) => [new("count", "How many", RuleBlockSlotKind.Number, "3", [], 1, 9)];
        public string Says(RuleBlockRow row) => "Deal 3.";
        public bool Set(RuleBlockRow row, string key, string value) => false;
        public bool Step(RuleBlockRow row, RuleBlockSlot slot, int by) => false;
        public bool Add(string paletteKey, RuleBlockRow? at) => false;
        public bool Move(RuleBlockRow row, int by) => false;
        public bool MoveTo(RuleBlockRow row, RuleBlockRow target) => false;
        public bool Delete(RuleBlockRow row) => false;
        public bool SetOpen(RuleBlockRow row, bool open) => false;
        public bool MakeItMine(RuleBlockRow row) => false;
        public bool GoBack(RuleBlockRow row) => false;
        public void Reload(string text) { }
    }

    private static void DrawPanel(RuleBlocksPanel panel)
    {
        ImGui.SetNextWindowPos(new Brutal.Numerics.float2(0, 0));
        ImGui.SetNextWindowSize(new Brutal.Numerics.float2(1280, 800));
        if (Ui.Begin("Rule blocks###rule-blocks-proof"u8)) panel.Draw();
        Ui.End();
    }

    [Fact]
    public void Draws_named_blocks_slots_and_palette_and_the_keyboard_moves_and_takes_out()
    {
        var (launcher, w, panel, _) = Open();
        using var _l = launcher;
        using var host = new HeadlessHost(1280, 800);
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(() => DrawPanel(panel), 3);
        var nodes = Ui.Snapshot().Nodes;
        foreach (var name in new[] { "Say something", "Repeat", "Wave", "Less times, now 2", "More times, now 2", "Words", "Undo", "Check", "Move up", "Take out" })
            Assert.Contains(nodes, n => n.Label == name);
        Assert.Contains(nodes, n => n.Label == "Say \"Hello\" 2 times.");
        Assert.True(host.Covered(0, 0, 1280, 800) > 0);

        // Click a palette block: it's added after the chosen block, and chosen.
        Assert.True(panel.Select(RowTitled(panel, "Say").Id));
        var palette = Assert.Single(Ui.Snapshot().Nodes, n => n.Label == "Say something" && n.Role == UiRole.Button);
        host.Click(() => DrawPanel(panel), palette.Bounds.X + 4, palette.Bounds.Y + 4);
        Assert.Equal(3, panel.Rows.Count(r => r.Title == "Say"));
        var chosen = panel.Selected;
        Assert.Equal(2, panel.Rows.ToList().FindIndex(r => r.Id == chosen));

        // Alt+Up moves it, Delete takes it out (with the stack focused).
        var added = Ui.Snapshot().Nodes.Where(n => n.Label == "Say").ElementAt(1);
        host.Click(() => DrawPanel(panel), added.Bounds.X + 4, added.Bounds.Y + 4);
        Assert.Equal(chosen, panel.Selected);
        host.Press(() => DrawPanel(panel), ImGuiKey.UpArrow, alt: true);
        Assert.Equal(1, panel.Rows.ToList().FindIndex(r => r.Id == panel.Selected));
        Assert.Equal("Hi", panel.Document!.Slots(panel.SelectedRow!).First(s => s.Key == "words").Shown);
        host.Press(() => DrawPanel(panel), ImGuiKey.Delete);
        Assert.Equal(2, panel.Rows.Count(r => r.Title == "Say"));
    }

    [Fact]
    public void Tab_reaches_every_block_control_in_order_and_each_is_named_for_a_screen_reader()
    {
        var (launcher, w, panel, _) = Open();
        using var _l = launcher;
        using var host = new HeadlessHost(1280, 800);
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(() => DrawPanel(panel), 3);
        var nodes = Ui.Snapshot().Nodes;
        // Every control has a name; a block's handle carries what the block does; the number buttons say the number; Open names its block.
        Assert.All(nodes.Where(n => n.Role is UiRole.Button or UiRole.Combo or UiRole.TextField or UiRole.ListItem or UiRole.Checkbox),
            n => Assert.False(string.IsNullOrWhiteSpace(n.Label), $"{n.Role} at {n.Bounds} has no name"));
        Assert.Contains(nodes, n => n.Role == UiRole.ListItem && n.Label == "Say" && n.Tooltip == "Say \"Hello\" 2 times.");
        Assert.Contains(nodes, n => n.Label == "Less times, now 2");
        Assert.Contains(nodes, n => n.Label == "Open Wave");
        Assert.DoesNotContain(nodes, n => n.Label == "Open");

        // From the toolbar, Tab goes through the palette, then each block and its slots, in order, and round again.
        var undo = Assert.Single(nodes, n => n.Label == "Undo");
        host.Click(() => DrawPanel(panel), undo.Bounds.X + 2, undo.Bounds.Y + 2);
        var order = new List<string>(); string? chosenOnTab = null;
        for (var i = 0; i < 40; i++)
        {
            host.Press(() => DrawPanel(panel), ImGuiKey.Tab);
            order.Add(Ui.Snapshot().Nodes.Where(n => n.Focused).Select(n => n.Label).FirstOrDefault() ?? "");
            if (panel.SelectedRow?.Title == "Repeat") chosenOnTab = "Repeat";
        }
        string[] wanted = ["Group", "Say something", "Repeat", "Wave", "When the game starts", "Say", "Words", "Less times, now 2", "More times, now 2", "Says it to", "Repeat", "Less times, now 3"];
        var at = order.IndexOf("Group");
        Assert.True(at >= 0, string.Join(" / ", order));
        Assert.Equal(wanted, order.Skip(at).Take(wanted.Length));
        Assert.Contains("Open Wave", order);
        Assert.DoesNotContain("", order.Skip(at).Take(20));   // no stop where nothing is focused

        // Tab onto a block chooses it. Delete with a palette block focused leaves the stack alone.
        Assert.Equal("Repeat", chosenOnTab);
        var count = panel.Rows.Count;
        for (var i = 0; i < 60 && !Ui.Snapshot().Nodes.Any(n => n.Focused && n.Label == "Say something"); i++) host.Press(() => DrawPanel(panel), ImGuiKey.Tab);
        Assert.Contains(Ui.Snapshot().Nodes, n => n.Focused && n.Label == "Say something");
        host.Press(() => DrawPanel(panel), ImGuiKey.Delete);
        Assert.Equal(count, panel.Rows.Count);
    }
}
