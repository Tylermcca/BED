using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>Search through a whole file, and going to what it found: past folds and pages, with the document left alone.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class DataSearchTests
{
    private static DataDocument Doc(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    [Fact]
    public void It_finds_names_and_plain_values_however_deep_in_document_order_ignoring_case()
    {
        var d = Doc("""{"a":{"b":[{"Needle":1},{"c":"a needle here"}]},"d":{"e/f":null,"g":true,"h":12.5},"needle2":"x"}""");
        Assert.Equal(["/a/b/0/Needle", "/a/b/1/c", "/needle2"], DataSearch.Find(d, "NEEDLE").Places.Select(p => p.Pointer));
        Assert.Equal(["/d/e~1f"], DataSearch.Find(d, "e/f").Places.Select(p => p.Pointer));   // a key with a slash in it
        Assert.Equal(["/d/e~1f"], DataSearch.Find(d, "null").Places.Select(p => p.Pointer));
        Assert.Equal(["/d/g"], DataSearch.Find(d, "true").Places.Select(p => p.Pointer));
        Assert.Equal(["/d/h"], DataSearch.Find(d, "12.5").Places.Select(p => p.Pointer));
        Assert.Empty(DataSearch.Find(d, "   ").Places);
        Assert.Empty(DataSearch.Find(d, "absent").Places);
    }

    [Fact]
    public void A_limit_says_there_is_more_and_only_when_there_is()
    {
        var d = Doc("""{"x":["n","n","n"]}""");
        var (some, more) = DataSearch.Find(d, "n", 2);
        Assert.Equal(2, some.Count);
        Assert.True(more);
        (some, more) = DataSearch.Find(d, "n", 3);
        Assert.Equal(3, some.Count);
        Assert.False(more);
    }

    private const string Schema = """
        {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{
          "label":{"type":"string"},
          "deep":{"type":"object","properties":{"list":{"type":"array","items":{"type":"object","properties":{"note":{"type":"string"}}}}}}}}}}}
        """;

    private static (EditorWindow<DataDocument> Window, HeadlessHost Host, DocumentSession<DataDocument> Session, Inspector<DataDocument> Inspector) Open(string json)
    {
        var schemas = new JsonSchemas().Add("sample", Schema);
        var session = DataKit.Session(new DataFormat().Read(json), schemas, "sample");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        var inspector = new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "sample"));
        var host = new HeadlessHost(1400, 2400) { Drawing = false };
        var window = new EditorWindow<DataDocument>(editor, inspector) { Searcher = (d, t) => DataSearch.Find(d, t) };
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(window.DrawDocked, 5);
        return (window, host, session, inspector);
    }

    private static string Long()
    {
        var notes = string.Join(",", Enumerable.Range(0, 300).Select(i => i == 217 ? "{\"note\":\"the needle\"}" : $"{{\"note\":\"n{i}\"}}"));
        return "{\"items\":[{\"label\":\"first\"},{\"label\":\"second\",\"deep\":{\"list\":[" + notes + "]}}]}";
    }

    [Fact]
    public void Going_to_a_find_selects_its_holder_opens_folds_turns_the_page_and_leaves_the_document_alone()
    {
        var (window, host, session, inspector) = Open(Long());
        using (host)
        {
            var before = session.Current;
            Assert.Equal(1, window.Find("needle"));
            var place = Assert.Single(window.Found);
            Assert.Equal("/items/1/deep/list/217/note", place.Pointer);

            Assert.True(window.Reveal(place.Pointer), window.Status);
            host.Frames(window.DrawDocked, 4);

            Assert.Equal("/items/1", window.Editor.Primary);
            var deep = inspector.Rows().Single(r => r.Id == "deep");
            var list = inspector.Nested(deep).Single(r => r.Id.EndsWith("/list", StringComparison.Ordinal));
            var item = inspector.Nested(list)[217];
            var note = inspector.Nested(item).Single();
            Assert.NotNull(window.InspectorPlace(note.Id));   // drawn: its page, its folds
            Assert.Null(window.InspectorPlace(inspector.Nested(list)[0].Id));
            Assert.Same(before, session.Current);
            Assert.Equal(0, session.History.UndoCount);
            Assert.False(session.Dirty);
        }
    }

    [Fact]
    public void The_box_searches_by_keyboard_and_a_result_goes_there_by_click()
    {
        var (window, host, session, inspector) = Open(Long());
        using (host)
        {
            var box = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.TextField && n.Label == "Find in this file");
            Assert.True(box.Enabled);
            Assert.False(box.Bounds.IsEmpty);
            host.Click(window.DrawDocked, box.Bounds.X + box.Bounds.Width / 2, box.Bounds.Y + box.Bounds.Height / 2);
            host.Frames(window.DrawDocked, 2);
            host.Type(window.DrawDocked, "needle");
            host.Press(window.DrawDocked, ImGuiKey.Enter);
            host.Frames(window.DrawDocked, 3);
            var found = Assert.Single(window.Found);
            var result = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == found.Preview);
            Assert.True(result.Enabled);
            Assert.False(result.Bounds.IsEmpty);
            host.Click(window.DrawDocked, result.Bounds.X + result.Bounds.Width / 2, result.Bounds.Y + result.Bounds.Height / 2);
            host.Frames(window.DrawDocked, 4);
            Assert.Equal("/items/1", window.Editor.Primary);
            Assert.Equal(0, session.History.UndoCount);
        }
    }

    [Fact]
    public void A_find_goes_stale_when_the_document_changes_and_a_place_that_has_gone_says_so()
    {
        var (window, host, session, _) = Open(Long());
        using (host)
        {
            window.Find("needle");
            var pointer = window.Found[0].Pointer;
            Assert.True(session.Execute(new RemoveItem("/items/1/deep/list/0")));
            Assert.Empty(window.Found);   // an index may mean something else now

            Assert.False(window.Reveal("/nothing/here"));
            Assert.False(string.IsNullOrEmpty(window.Status));
            Assert.False(window.Reveal("/items/1/deep/absent"));
            Assert.Contains("no row", window.Status);
            Assert.True(window.Reveal(pointer.Replace("/217/", "/216/", StringComparison.Ordinal)), window.Status);
        }
    }

    [Fact]
    public void With_no_searcher_there_is_no_box()
    {
        var schemas = new JsonSchemas().Add("sample", Schema);
        var session = DataKit.Session(new DataFormat().Read("""{"items":[{"label":"a"}]}"""), schemas, "sample");
        var editor = new EditorModel<DataDocument>(session, DataEditing.Outline);
        using var host = new HeadlessHost(800, 600) { Drawing = false };
        var window = new EditorWindow<DataDocument>(editor, new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas, "sample")));
        EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        host.Frames(window.DrawDocked, 3);
        Assert.Null(window.InspectorPlace("", InspectorPart.Find));
        Assert.Equal(0, window.Find("a"));
    }
}
