using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Xunit.Abstractions;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace Brutal.Authoring.Tests;

/// <summary>
/// A179's per-frame hot spots 2, 3 and 6 (docs/plan/frame_profile.md): an unchanged frame no longer works out the
/// inspector's rows, the outline or a table cell's links again, and an edit, undo, selection, focus or schema change still
/// shows what's new. INVENTED neutral generated data; call counts are the guard, allocations are reported alongside.
/// </summary>
[Collection(ImGuiCollection.Name)]
public sealed class FrameCacheTests(ITestOutputHelper output) : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "frame-cache-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static JsonElement J(object value) => JsonSerializer.SerializeToElement(value);

    // The best of five runs of the average time of one call, so a descheduled run doesn't count.
    private static double Microseconds(Action act, int times)
    {
        act();
        var best = double.MaxValue;
        for (var run = 0; run < 5; run++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < times; i++) act();
            best = Math.Min(best, watch.Elapsed.TotalMicroseconds / times);
        }
        return best;
    }

    private static long Allocated(Action act, int times)
    {
        act();
        var before = GC.GetTotalAllocatedBytes(true);
        for (var i = 0; i < times; i++) act();
        return (GC.GetTotalAllocatedBytes(true) - before) / times;
    }

    // ---- rank 2: the inspector's selected type and top rows ----

    // A schema whose items take one of two shapes by their kind, through references and a conditional, so finding an item's
    // rows walks real branches; each item holds a nested record of many fields and a long list.
    private static readonly string StorySchema = new JsonObject
    {
        ["$defs"] = new JsonObject
        {
            ["body"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(Enumerable.Range(0, 40).Select(i => KeyValuePair.Create<string, JsonNode?>("f" + i, new JsonObject { ["type"] = "string", ["title"] = "Field " + i }))) },
            ["line"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["speaker"] = new JsonObject { ["type"] = "string" }, ["text"] = new JsonObject { ["type"] = "string" } } },
            ["scene"] = new JsonObject { ["type"] = "object", ["required"] = new JsonArray("kind"), ["properties"] = new JsonObject { ["kind"] = new JsonObject { ["const"] = "scene" }, ["mood"] = new JsonObject { ["enum"] = new JsonArray("calm", "tense") } } },
            ["note"] = new JsonObject { ["type"] = "object", ["required"] = new JsonArray("kind"), ["properties"] = new JsonObject { ["kind"] = new JsonObject { ["const"] = "note" }, ["weight"] = new JsonObject { ["type"] = "integer" } } },
        },
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["templates"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["id"] = new JsonObject { ["type"] = "string" }, ["name"] = new JsonObject { ["type"] = "string", ["title"] = "Name" },
                        ["kind"] = new JsonObject { ["enum"] = new JsonArray("scene", "note") },
                        ["body"] = new JsonObject { ["$ref"] = "#/$defs/body" },
                        ["lines"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["$ref"] = "#/$defs/line" } },
                    },
                    ["oneOf"] = new JsonArray(new JsonObject { ["$ref"] = "#/$defs/scene" }, new JsonObject { ["$ref"] = "#/$defs/note" }),
                    ["if"] = new JsonObject { ["properties"] = new JsonObject { ["kind"] = new JsonObject { ["const"] = "scene" } } },
                    ["then"] = new JsonObject { ["properties"] = new JsonObject { ["cue"] = new JsonObject { ["type"] = "string", ["description"] = "When it starts" } } },
                },
            },
        },
    }.ToJsonString();

    private static DataDocument Story(int items, int lines) => new DataFormat().Read(new JsonObject
    {
        ["templates"] = new JsonArray(Enumerable.Range(0, items).Select(i => (JsonNode?)new JsonObject
        {
            ["id"] = "t" + i, ["name"] = "Template " + i, ["kind"] = i % 2 == 0 ? "scene" : "note",
            ["body"] = new JsonObject(Enumerable.Range(0, 40).Select(f => KeyValuePair.Create<string, JsonNode?>("f" + f, "v" + f))),
            ["lines"] = new JsonArray(Enumerable.Range(0, lines).Select(l => (JsonNode?)new JsonObject { ["speaker"] = "s" + l, ["text"] = "line " + l }).ToArray()),
        }).ToArray()),
    }.ToJsonString());

    // Counts what the inspector asks of the schema-driven type, and passes everything through.
    private sealed class CountingType(IObjectType<DataDocument> inner) : IObjectType<DataDocument>
    {
        public int RowsAsked, NestedAsked;
        public bool Unnumbered;
        public long? RowsVersion => Unnumbered ? null : inner.RowsVersion;
        public string Id => inner.Id;
        public string Label => inner.Label;
        public IReadOnlyList<InspectorRow> Rows(DataDocument d, IReadOnlyList<string> ids) { RowsAsked++; return inner.Rows(d, ids); }
        public IReadOnlyList<InspectorRow> NestedRows(DataDocument d, IReadOnlyList<string> ids, string p) { NestedAsked++; return inner.NestedRows(d, ids, p); }
        public IReadOnlyList<ICommand<DataDocument>>? AddCommands(DataDocument d, IReadOnlyList<string> ids, string p) => inner.AddCommands(d, ids, p);
        public IReadOnlyList<ICommand<DataDocument>>? RemoveCommands(DataDocument d, IReadOnlyList<string> ids, string p) => inner.RemoveCommands(d, ids, p);
        public IReadOnlyList<ICommand<DataDocument>>? AddHereCommands(DataDocument d, IReadOnlyList<string> ids) => inner.AddHereCommands(d, ids);
        public IReadOnlyList<ICommand<DataDocument>>? RemoveHereCommands(DataDocument d, IReadOnlyList<string> ids) => inner.RemoveHereCommands(d, ids);
        public IReadOnlyList<ICommand<DataDocument>>? EditCommands(DataDocument d, IReadOnlyList<string> ids, string p, object? v, bool reset) => inner.EditCommands(d, ids, p, v, reset);
    }

    private sealed class StoryRig : IDisposable
    {
        internal readonly JsonSchemas Schemas = new JsonSchemas().Add("story.schema.json", StorySchema);
        internal readonly DocumentSession<DataDocument> Session;
        internal readonly EditorModel<DataDocument> Editor;
        internal readonly CountingType Type;
        internal readonly Inspector<DataDocument> Inspector;
        internal readonly EditorWindow<DataDocument> Window;
        internal readonly HeadlessHost Host = new(1600, 1200) { Drawing = false };
        internal StoryRig(int items = 20, int lines = 120, bool counting = true)
        {
            Session = DataKit.Session(Story(items, lines), Schemas, "story.schema.json");
            Editor = new EditorModel<DataDocument>(Session, DataEditing.OutlineFor(Schemas, "story.schema.json"));
            var type = DataEditing.ObjectType(Schemas, "story.schema.json");
            Type = new CountingType(type);
            Inspector = new Inspector<DataDocument>(Editor).Add(counting ? Type : type);
            Window = new EditorWindow<DataDocument>(Editor, Inspector);
        }
        internal void Draw() => Window.Draw();
        internal void Frames(int n = 3) => Host.Frames(Draw, n);
        internal InspectorRow Row(string id) => Inspector.Rows().Single(r => r.Id == id);
        public void Dispose() => Host.Dispose();
    }

    [Fact]
    public void An_unchanged_selected_frame_asks_the_type_for_no_rows()
    {
        using var r = new StoryRig();
        Assert.True(r.Editor.Select("/templates/0"));
        r.Frames(10);
        r.Type.RowsAsked = r.Type.NestedAsked = 0;
        r.Frames(10);
        output.WriteLine($"10 unchanged selected frames: Rows asked {r.Type.RowsAsked}, NestedRows asked {r.Type.NestedAsked}");
        Assert.Equal(0, r.Type.RowsAsked);
        Assert.Equal(0, r.Type.NestedAsked);
        Assert.Equal(0, r.Session.Revision);
    }

    [Fact]
    public void An_unchanged_selected_frame_allocates_far_less_than_working_the_rows_out()
    {
        using var r = new StoryRig(counting: false);
        Assert.True(r.Editor.Select("/templates/0"));
        r.Frames(10);
        var frame = Allocated(() => r.Host.Frame(r.Draw), 20);
        // What one frame's row discovery costs when nothing is kept: the type's own rows, then each top row's nested rows.
        var type = DataEditing.ObjectType(r.Schemas, "story.schema.json");
        var discovery = Allocated(() => { foreach (var row in type.Rows(r.Session.Current, r.Editor.Selection)) _ = type.NestedRows(r.Session.Current, r.Editor.Selection, row.Id); }, 20);
        output.WriteLine($"selected frame allocates {frame} bytes and takes {Microseconds(() => r.Host.Frame(r.Draw), 20):F0} us; one uncached row discovery allocates {discovery} bytes");
        Assert.True(frame < discovery, $"a frame ({frame} bytes) should cost less than working the rows out once ({discovery} bytes)");
    }

    [Fact]
    public void Edit_undo_selection_focus_and_schema_changes_still_refresh_the_inspector()
    {
        using var r = new StoryRig();
        Assert.True(r.Editor.Select("/templates/0"));
        r.Frames();
        Assert.Equal("Template 0", r.Row("name").Value);
        Assert.Contains(r.Inspector.Rows(), x => x.Id == "cue");   // the conditional's branch for a scene

        // An edit makes a new document, and the rows follow it; undo brings the old one back.
        Assert.True(r.Inspector.Set("name", "Renamed"));
        r.Frames(1);
        Assert.Equal("Renamed", r.Row("name").Value);
        Assert.True(r.Session.Undo());
        r.Frames(1);
        Assert.Equal("Template 0", r.Row("name").Value);

        // A branch-sensitive edit: a note takes no cue.
        Assert.True(r.Inspector.Set("kind", "note"));
        Assert.DoesNotContain(r.Inspector.Rows(), x => x.Id == "cue");
        Assert.True(r.Session.Undo());
        Assert.Contains(r.Inspector.Rows(), x => x.Id == "cue");

        // Another selection shows its own rows.
        Assert.True(r.Editor.Select("/templates/1"));
        r.Frames(1);
        Assert.Equal("Template 1", r.Row("name").Value);
        Assert.DoesNotContain(r.Inspector.Rows(), x => x.Id == "cue");

        // Focus elsewhere locks the rows; focus out unlocks them, with the same document.
        var document = r.Session.Current;
        Assert.True(r.Row("name").Editable);
        Assert.True(r.Editor.FocusOn("/templates/0"));
        r.Frames(1);
        Assert.False(r.Row("name").Editable);
        Assert.False(r.Inspector.Nested(r.Row("body")).First().Editable);
        Assert.True(r.Editor.FocusOut());
        r.Frames(1);
        Assert.True(r.Row("name").Editable);
        Assert.Same(document, r.Session.Current);

        // A replaced schema relabels the row without any edit.
        var replaced = JsonNode.Parse(StorySchema)!;
        replaced["properties"]!["templates"]!["items"]!["properties"]!["name"]!["title"] = "Title";
        r.Schemas.Add("story.schema.json", replaced.ToJsonString());
        r.Frames(1);
        Assert.Equal("Title", r.Row("name").Label);
        Assert.Same(document, r.Session.Current);

        // A type that numbers nothing stays live: it's asked every time.
        r.Type.Unnumbered = true;
        r.Type.RowsAsked = 0;
        r.Frames(2);
        Assert.True(r.Type.RowsAsked >= 2, $"an unnumbered type was asked {r.Type.RowsAsked} times in two frames");
        Assert.Equal(0, r.Session.History.UndoCount);
    }

    private sealed class WarnsOnName : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument d) =>
            Enumerable.Range(0, d.At("/templates")!.Value.GetArrayLength()).Where(i => d.At($"/templates/{i}/name")!.Value.GetString() == "warn")
                .Select(i => Issue.Warning("a warned name", $"/templates/{i}", $"/templates/{i}/name")).ToList();
    }

    [Fact]
    public void The_selections_issues_follow_the_selection_and_the_document()
    {
        var schemas = new JsonSchemas().Add("story.schema.json", StorySchema);
        var session = new DocumentSession<DataDocument>(new DataFormat(), new WarnsOnName(), DataKit.Catalog(), Story(3, 2));
        var editor = new EditorModel<DataDocument>(session, DataEditing.OutlineFor(schemas, "story.schema.json"));
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "story.schema.json"));
        Assert.True(editor.Select("/templates/1"));
        Assert.Empty(inspector.Issues());
        Assert.True(inspector.Set("name", "warn"));
        var mine = Assert.Single(inspector.Issues());   // a new document is validated again at once
        Assert.Equal("/templates/1", mine.ObjectId);
        Assert.Same(inspector.Issues(), inspector.Issues());   // the same answer while nothing changes
        Assert.True(editor.Select("/templates/0"));
        Assert.Empty(inspector.Issues());
        Assert.True(editor.Select("/templates/1"));
        Assert.Single(inspector.Issues());
        Assert.True(session.Undo());
        Assert.Empty(inspector.Issues());
    }

    // ---- rank 3: the outline ----

    // Counts what the outliner asks of the outline, and passes everything through; Bump changes its version.
    private sealed class CountingOutline(IDocumentOutline<DataDocument> inner) : IDocumentOutline<DataDocument>
    {
        public int IdsAsked, LabelsAsked;
        public readonly List<string> Asked = [];
        public readonly Dictionary<string, string?> Said = new(StringComparer.Ordinal);
        public long Bump;
        public bool Unnumbered;
        public IEnumerable<string> ObjectIds(DataDocument d) { IdsAsked++; return inner.ObjectIds(d); }
        public string? Label(DataDocument d, string id) { LabelsAsked++; Asked.Add(id); return Said[id] = inner.Label(d, id); }
        public string? Parent(DataDocument d, string id) => inner.Parent(d, id);
        public long? Version => Unnumbered ? null : inner.Version + Bump;
        public void Reset() { IdsAsked = LabelsAsked = 0; Asked.Clear(); }
    }

    private const string CatalogSchema = """
        {"type":"object","properties":{
          "objects":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"},"display":{"type":"string"}}}},
          "rooms":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"}}}}}}
        """;

    private sealed class CatalogRig : IDisposable
    {
        internal readonly JsonSchemas Schemas = new JsonSchemas().Add("catalog.schema.json", CatalogSchema);
        internal readonly DocumentSession<DataDocument> Session;
        internal readonly CountingOutline Outline;
        internal readonly EditorModel<DataDocument> Editor;
        internal readonly EditorWindow<DataDocument> Window;
        internal readonly HeadlessHost Host = new(1280, 800) { Drawing = false };
        internal CatalogRig(int objects, bool counting = true)
        {
            var text = new JsonObject
            {
                ["objects"] = new JsonArray(Enumerable.Range(0, objects).Select(i => (JsonNode?)new JsonObject { ["id"] = "o" + i, ["name"] = "Object " + i, ["display"] = "Shown " + i }).ToArray()),
                ["rooms"] = new JsonArray(Enumerable.Range(0, 5).Select(i => (JsonNode?)new JsonObject { ["id"] = "r" + i, ["name"] = "Room " + i }).ToArray()),
            }.ToJsonString();
            Session = DataKit.Session(new DataFormat().Read(text), Schemas, "catalog.schema.json");
            var outline = DataEditing.OutlineFor(Schemas, "catalog.schema.json");
            Outline = new CountingOutline(outline);
            Editor = new EditorModel<DataDocument>(Session, counting ? Outline : outline);
            Window = new EditorWindow<DataDocument>(Editor);
        }
        internal void Draw() => Window.Draw();
        internal void Frames(int n = 3) => Host.Frames(Draw, n);
        public void Dispose() => Host.Dispose();
    }

    [Fact]
    public void An_unchanged_outline_frame_walks_and_labels_nothing_and_a_changed_one_labels_only_what_is_drawn()
    {
        using var r = new CatalogRig(3000);
        r.Frames(5);
        r.Outline.Reset();
        r.Frames(10);
        output.WriteLine($"10 unchanged frames, 3000 objects: ids asked {r.Outline.IdsAsked}, labels asked {r.Outline.LabelsAsked}");
        Assert.Equal(0, r.Outline.IdsAsked);
        Assert.Equal(0, r.Outline.LabelsAsked);

        Assert.True(r.Session.Execute(new SetValue("/objects/0/name", J("Moved"))));
        r.Outline.Reset();
        r.Frames(1);
        output.WriteLine($"the frame after an edit: ids asked {r.Outline.IdsAsked}, labels asked {r.Outline.LabelsAsked}");
        Assert.Equal(1, r.Outline.IdsAsked);
        Assert.InRange(r.Outline.LabelsAsked, 1, 200);   // the rows on screen, not 3,005
        Assert.Equal("Moved", r.Outline.Said["/objects/0"]);
    }

    [Fact]
    public void An_unchanged_outline_frame_allocates_far_less_than_labelling_it_whole()
    {
        using var r = new CatalogRig(3000, counting: false);
        r.Frames(5);
        var frame = Allocated(() => r.Host.Frame(r.Draw), 20);
        var outline = DataEditing.OutlineFor(r.Schemas, "catalog.schema.json");
        var whole = Allocated(() => { foreach (var id in outline.ObjectIds(r.Session.Current)) _ = outline.Label(r.Session.Current, id); }, 5);
        output.WriteLine($"outline frame allocates {frame} bytes and takes {Microseconds(() => r.Host.Frame(r.Draw), 20):F0} us; labelling the whole outline once allocates {whole} bytes");
        Assert.True(frame * 4 < whole, $"a frame ({frame} bytes) should cost far less than labelling every object ({whole} bytes)");
    }

    [Fact]
    public void Edit_undo_schema_focus_and_scrolling_still_refresh_the_outline()
    {
        using var r = new CatalogRig(3000);
        r.Frames(3);
        Assert.Equal("Object 0", r.Outline.Said["/objects/0"]);

        // An edit and its undo are labelled again.
        Assert.True(r.Session.Execute(new SetValue("/objects/0/name", J("Edited"))));
        r.Frames(1);
        Assert.Equal("Edited", r.Outline.Said["/objects/0"]);
        Assert.True(r.Session.Undo());
        r.Frames(1);
        Assert.Equal("Object 0", r.Outline.Said["/objects/0"]);

        // A schema naming another label field relabels without an edit.
        var document = r.Session.Current;
        var labelled = JsonNode.Parse(CatalogSchema)!;
        labelled["properties"]!["objects"]!["items"]!["x-label"] = "display";
        r.Schemas.Add("catalog.schema.json", labelled.ToJsonString());
        r.Outline.Reset();
        r.Frames(1);
        Assert.Equal("Shown 0", r.Outline.Said["/objects/0"]);
        Assert.Same(document, r.Session.Current);

        // Focus on a place shows only it and what's in it.
        Assert.True(r.Editor.FocusOn("/rooms"));
        r.Outline.Bump++;
        r.Outline.Reset();
        r.Frames(1);
        Assert.Contains("/rooms", r.Outline.Asked);
        Assert.All(r.Outline.Asked.Where(id => id != "/rooms"), id => Assert.StartsWith("/rooms/", id));
        Assert.True(r.Editor.FocusOut());
        r.Outline.Reset();
        r.Frames(1);
        Assert.Equal(1, r.Outline.IdsAsked);   // a change of focus works the tree out again

        // Scrolled to the end, the last rows are drawn and labelled.
        r.Outline.Bump++;
        r.Outline.Reset();
        r.Host.MoveMouse(r.Draw, 100, 300);
        for (var i = 0; i < 3; i++) { ImGui.GetIO().AddMouseWheelEvent(0, -1000000); r.Host.Frame(r.Draw); }
        Assert.Contains("/rooms/4", r.Outline.Asked);
        Assert.Contains("/objects/2999", r.Outline.Asked);
        Assert.True(r.Outline.LabelsAsked < 400, $"scrolling labelled {r.Outline.LabelsAsked} rows");

        // An outline that numbers nothing stays live.
        r.Outline.Unnumbered = true;
        r.Outline.Reset();
        r.Frames(2);
        Assert.Equal(2, r.Outline.IdsAsked);
        Assert.Equal(0, r.Session.History.UndoCount);
    }

    // ---- rank 6: table links ----

    private const string LinkSchema = """
        {"type":"object","properties":{
          "wares":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"}}}},
          "factories":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"output":{"type":"string","x-ref":"/wares"},"buyer":{"type":"string"}}}}}}
        """;

    private const string LinkMap = """
        {"game":"Neutral","labels":[],"references":[
          {"file":"factories.json","at":"/factories/*/buyer","target":{"file":"traders.json","list":"/traders","key":"id"},"evidence":"neutral"}]}
        """;

    private DataProject LinkProject(int wares, int factories)
    {
        var data = Path.Combine(root, "data");
        var schemas = Path.Combine(root, "schemas");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(schemas);
        File.WriteAllText(Path.Combine(schemas, "factories.schema.json"), LinkSchema);
        File.WriteAllText(Path.Combine(schemas, "traders.schema.json"), """{"type":"object"}""");
        var waresJson = new JsonArray(Enumerable.Range(0, wares).Select(i => (JsonNode?)new JsonObject { ["id"] = "w" + i, ["name"] = "Ware " + i }).ToArray());
        var factoriesJson = new JsonArray(Enumerable.Range(0, factories).Select(i => (JsonNode?)new JsonObject { ["id"] = "f" + i, ["output"] = "w" + (i * 7 % wares), ["buyer"] = "t" + (i % wares) }).ToArray());
        File.WriteAllText(Path.Combine(data, "factories.json"), new JsonObject { ["$schema"] = "factories.schema.json", ["wares"] = waresJson, ["factories"] = factoriesJson }.ToJsonString());
        var traders = new JsonArray(Enumerable.Range(0, wares).Select(i => (JsonNode?)new JsonObject { ["id"] = "t" + i }).ToArray());
        File.WriteAllText(Path.Combine(data, "traders.json"), new JsonObject { ["$schema"] = "traders.schema.json", ["traders"] = traders }.ToJsonString());
        var project = DataProject.Open(data, schemas);
        Assert.True(project.Find("factories.json")!.Open(), string.Join("; ", project.Find("factories.json")!.Problems));
        Assert.True(project.Find("traders.json")!.Open(), string.Join("; ", project.Find("traders.json")!.Problems));
        return project;
    }

    [Fact]
    public void A_schema_reference_asked_again_scans_nothing_and_follows_edits_undo_and_schema_changes()
    {
        var schemas = new JsonSchemas().Add("f.schema.json", LinkSchema);
        var wares = new JsonArray(Enumerable.Range(0, 5000).Select(i => (JsonNode?)new JsonObject { ["id"] = "w" + i }).ToArray());
        var factories = new JsonArray(new JsonObject { ["output"] = "w4000" });
        var session = DataKit.Session(new DataFormat().Read(new JsonObject { ["wares"] = wares, ["factories"] = factories }.ToJsonString()), schemas, "f.schema.json");
        var refs = DataEditing.SchemaReferences(schemas, "f.schema.json");
        string? Follow() => refs(session.Current, "/factories/0/output", session.Current.At("/factories/0/output")!.Value);
        Assert.Equal("/wares/4000", Follow());
        var repeated = Allocated(() => Follow(), 200);
        var first = Allocated(() => DataEditing.SchemaReferences(schemas, "f.schema.json")(session.Current, "/factories/0/output", session.Current.At("/factories/0/output")!.Value), 20);
        output.WriteLine($"a repeated schema reference allocates {repeated} bytes; a first one allocates {first} bytes");
        Assert.True(repeated < 1024, $"a repeated reference allocated {repeated} bytes: the target list was scanned again");

        Assert.True(session.Execute(new SetValue("/wares/4000/id", J("gone"))));
        Assert.Null(Follow());                                   // missing
        Assert.True(session.Undo());
        Assert.Equal("/wares/4000", Follow());
        Assert.True(session.Execute(new SetValue("/wares/1/id", J("w4000"))));
        Assert.Null(Follow());                                   // ambiguous: two items have it
        Assert.True(session.Undo());
        Assert.Equal("/wares/4000", Follow());

        var document = session.Current;
        schemas.Add("f.schema.json", LinkSchema.Replace("\"x-ref\":\"/wares\"", "\"description\":\"no longer a link\""));
        Assert.Null(Follow());                                   // the schema no longer says it's a link
        Assert.Same(document, session.Current);
    }

    [Fact]
    public void A_map_link_asked_again_scans_nothing_and_follows_its_target_files()
    {
        var project = LinkProject(5000, 10);
        var map = ReferenceMap.Read(LinkMap);
        var factories = project.Find("factories.json")!;
        ReferenceResult Follow() => map.Resolve(project, "factories.json", "/factories/3/buyer", factories.Session!.Current.At("/factories/3/buyer")!.Value);
        Assert.Equal(new ReferenceTarget("traders.json", "/traders/3"), Follow().Target);
        // The same question by meaning (ReferenceIntent.Value) is answered afresh each time: the cost of scanning the target.
        ReferenceResult Afresh() => map.Resolve(project, "factories.json", "/factories/3/buyer", factories.Session!.Current.At("/factories/3/buyer")!.Value, ReferenceIntent.Value);
        Assert.Equal(Follow(), Afresh());
        var repeated = Allocated(() => Follow(), 200);
        var scanned = Allocated(() => Afresh(), 200);
        output.WriteLine($"a repeated map link allocates {repeated} bytes and takes {Microseconds(() => Follow(), 500):F2} us; resolving it afresh allocates {scanned} bytes and takes {Microseconds(() => Afresh(), 500):F2} us");
        Assert.True(repeated * 3 < scanned, $"a repeated map link allocated {repeated} bytes against {scanned} to resolve it afresh: it was resolved again");

        var traders = project.Find("traders.json")!.Session!;
        Assert.True(traders.Execute(new SetValue("/traders/3/id", J("elsewhere"))));
        Assert.Equal(ReferenceOutcome.Missing, Follow().Outcome);
        Assert.True(traders.Undo());
        Assert.Equal(ReferenceOutcome.Found, Follow().Outcome);
        Assert.True(traders.Execute(new SetValue("/traders/9/id", J("t3"))));
        Assert.Equal(ReferenceOutcome.Ambiguous, Follow().Outcome);
        Assert.True(traders.Undo());
        Assert.Equal(new ReferenceTarget("traders.json", "/traders/3"), Follow().Target);

        // The source value changing is a different question.
        Assert.True(factories.Session!.Execute(new SetValue("/factories/3/buyer", J("t4"))));
        Assert.Equal(new ReferenceTarget("traders.json", "/traders/4"), Follow().Target);
        Assert.True(factories.Session.Undo());

        // A target file that's gone from the project isn't followed; back again, it is.
        Assert.True(project.Rename("traders.json", "dealers.json", out var why), why);
        Assert.Equal(ReferenceOutcome.NotLoaded, Follow().Outcome);
        Assert.True(project.Rename("dealers.json", "traders.json", out why), why);
        Assert.True(project.Find("traders.json")!.Open());
        Assert.Equal(new ReferenceTarget("traders.json", "/traders/3"), Follow().Target);
    }

    private sealed class TableRig : IDisposable
    {
        internal readonly DataProject Project;
        internal readonly EditorModel<DataDocument> Editor;
        internal readonly DataTableView Table;
        internal readonly HeadlessHost Host = new(1400, 900) { Drawing = false };
        internal int LinksAsked;
        internal TableRig(DataProject project)
        {
            Project = project;
            var file = project.Find("factories.json")!;
            var map = ReferenceMap.Read(LinkMap);
            Editor = new EditorModel<DataDocument>(file.Session!, DataEditing.OutlineFor(project.Schemas, file.Schema!));
            Table = new DataTableView(Editor, DataEditing.ObjectType(project.Schemas, file.Schema!))
            {
                References = DataEditing.SchemaReferences(project.Schemas, file.Schema!),
                File = file.Name,
                Links = (at, value) => { LinksAsked++; return map.Resolve(project, file.Name, at, value) is { Outcome: ReferenceOutcome.Found, Target: { } t } ? t : null; },
                List = "/factories",
            };
        }
        internal void Draw()
        {
            ImGui.SetNextWindowPos(new Brutal.Numerics.float2(0, 0), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Brutal.Numerics.float2(1400, 900), ImGuiCond.Always);
            ImGui.Begin("Frame cache table"u8);
            Table.Draw();
            ImGui.End();
        }
        public void Dispose() => Host.Dispose();
    }

    [Fact]
    public void An_unchanged_table_frame_reuses_its_links_and_item_count_and_an_edit_refreshes_them()
    {
        using var r = new TableRig(LinkProject(5000, 2000));
        r.Host.Frames(r.Draw, 5);
        var frame = Allocated(() => r.Host.Frame(r.Draw), 20);
        output.WriteLine($"an unchanged table frame allocates {frame} bytes and takes {Microseconds(() => r.Host.Frame(r.Draw), 20):F0} us");
        Assert.True(frame < 2_000_000, $"an unchanged table frame allocated {frame} bytes");
        Assert.Same(r.Table.Items(), r.Table.Items());
        Assert.Equal("/wares/0", r.Table.LinkOf("/factories/0", "output")!.Pointer);

        // An edit to the target list in the same file: the link goes, and comes back on undo; the count follows too.
        var items = r.Table.Items();
        Assert.True(r.Editor.Session.Execute(new SetValue("/wares/0/id", J("renamed"))));
        r.Host.Frame(r.Draw);
        Assert.Null(r.Table.Target("/factories/0", "output"));
        Assert.True(r.Editor.Session.Undo());
        r.Host.Frame(r.Draw);
        Assert.Equal("/wares/0", r.Table.Target("/factories/0", "output"));
        Assert.True(r.Editor.Session.Execute(new InsertItem("/factories", 0, J(new { id = "new", output = "w1" }))));
        Assert.NotSame(items, r.Table.Items());
        Assert.Equal(2001, r.Table.Items().Count);
        Assert.True(r.Editor.Session.Undo());
        Assert.Equal(2000, r.Table.Items().Count);

        // An edit in the other file: the map's link follows it.
        var traders = r.Project.Find("traders.json")!.Session!;
        Assert.True(traders.Execute(new SetValue("/traders/0/id", J("away"))));
        Assert.Null(r.Table.LinkOf("/factories/0", "buyer"));
        Assert.True(traders.Undo());
        Assert.Equal(new ReferenceTarget("traders.json", "/traders/0"), r.Table.LinkOf("/factories/0", "buyer"));
        Assert.Equal(0, r.Editor.Session.History.UndoCount);
    }
}
