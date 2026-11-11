using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

/// <summary>A data project in the editor window: the Files panel, one editor per file, and saving through the
/// project's rules (A038's piece P, in the window).</summary>
[Collection(ImGuiCollection.Name)]
public sealed class DataWorkspaceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "dataworkspace-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(root, "data");

    public DataWorkspaceTests()
    {
        var schemas = Path.Combine(root, "schemas");
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(schemas);
        File.WriteAllText(Path.Combine(schemas, "wares.schema.json"), """{"type":"object","properties":{"wares":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"price":{"type":"number","minimum":0}}}}}}""");
        File.WriteAllText(Path.Combine(schemas, "crew.schema.json"), """{"type":"object","properties":{"crew":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"wage":{"type":"integer"}}}}}}""");
        File.WriteAllText(Path.Combine(Data, "wares.json"), """{"wares":[{"name":"Rope","price":2}]}""");
        File.WriteAllText(Path.Combine(Data, "crew.json"), """{"crew":[{"name":"Ana","wage":40}]}""");
        File.WriteAllText(Path.Combine(Data, "broken.json"), "{ nope");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private DataWorkspace Open(bool readOnly = false) => new(DataProject.Open(Data, Path.Combine(root, "schemas"), readOnly));

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void With_no_file_shown_it_draws_the_file_list()
    {
        var w = Open();
        using var host = new HeadlessHost(1200, 700);
        EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        host.Frames(w.Draw, 3);
        Assert.Null(w.Active);
        Assert.True(host.Covered(0, 0, 1200, 700) > 500, "the file list drew");
    }

    [Fact]
    public void Showing_a_file_gives_it_the_full_editor_and_a_bad_file_says_why()
    {
        var w = Open();
        Assert.True(w.Show("wares.json"));
        Assert.NotNull(w.Window);
        Assert.True(w.Window!.Editor.IsOpen("editor.table"));
        Assert.True(w.Window.Editor.IsOpen("project.files"));
        Assert.False(w.Show("broken.json"));
        Assert.Contains("can't be opened", w.Said);
        Assert.Equal("wares.json", w.Active!.Name);   // what was shown stays shown
        Assert.False(w.Show("nothing.json"));
    }

    [Fact]
    public void Switching_files_keeps_each_ones_edits_and_undo()
    {
        var w = Open();
        Assert.True(w.Show("wares.json"));
        var wares = w.Window!;
        Assert.True(wares.Editor.Session.Execute(new SetValue("/wares/0/price", Json("5"))));
        Assert.True(w.Show("crew.json"));
        Assert.False(w.Window!.Editor.Session.Dirty);
        Assert.True(w.Show("wares.json"));
        Assert.Same(wares, w.Window);
        Assert.True(w.Window.Editor.Session.Dirty);
        Assert.True(w.Window.Run("editor.undo"));
        Assert.Equal(2, w.Window.Editor.Session.Current.At("/wares/0/price")!.Value.GetDouble());
    }

    [Fact]
    public void Save_all_writes_every_edited_file_and_reports_one_that_changed_outside()
    {
        var w = Open();
        Assert.True(w.Show("wares.json"));
        Assert.True(w.Window!.Editor.Session.Execute(new SetValue("/wares/0/price", Json("5"))));
        Assert.True(w.Show("crew.json"));
        Assert.True(w.Window!.Editor.Session.Execute(new SetValue("/crew/0/wage", Json("45"))));
        File.WriteAllText(Path.Combine(Data, "crew.json"), """{"crew":[{"name":"Ana","wage":41}]}""");
        Assert.False(w.SaveAll());
        Assert.Contains("crew.json was changed outside the editor", w.Said);
        Assert.Equal(5, JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "wares.json"))).RootElement.GetProperty("wares")[0].GetProperty("price").GetDouble());
        Assert.Contains("41", File.ReadAllText(Path.Combine(Data, "crew.json")));
    }

    [Fact]
    public void A_read_only_project_refuses_every_way_to_save()
    {
        var w = Open(readOnly: true);
        Assert.True(w.Show("wares.json"));
        var window = w.Window!;
        Assert.True(window.Editor.Session.Execute(new SetValue("/wares/0/price", Json("5"))));
        Assert.False(window.Run("editor.save"));
        Assert.Contains("read-only", window.Status);
        Assert.False(window.SaveAs(Path.Combine(root, "elsewhere.json")));
        Assert.False(File.Exists(Path.Combine(root, "elsewhere.json")));
        var saveAll = Assert.Single(window.PaletteEntries("save all files"));
        Assert.Equal("the project is open read-only", saveAll.Unavailable);
        Assert.Contains("\"price\":2", File.ReadAllText(Path.Combine(Data, "wares.json")));
    }

    [Fact]
    public void A_shown_file_draws_docked_headless()
    {
        var w = Open();
        Assert.True(w.Show("wares.json"));
        using var host = new HeadlessHost(1400, 800);
        EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        host.Frames(w.Draw, 4);
        Assert.True(host.Covered(0, 0, 1400, 800) > 2000, "the editor drew");
        if (Environment.GetEnvironmentVariable("BRUTAL_AUTHORING_SHOTS") is { Length: > 0 } d)
            host.Save(Path.Combine(Directory.CreateDirectory(d).FullName, "31_data_workspace.png"));
    }

    [Fact]
    public void A_reference_map_labels_rows_and_its_links_cross_files_and_come_back()
    {
        File.WriteAllText(Path.Combine(root, "schemas", "jobs.schema.json"), "{}");
        File.WriteAllText(Path.Combine(Data, "jobs.json"), """{"jobs":[{"code":"J1","ware":"Rope","crew":"Ana"},{"code":"J2","ware":"Silk"}]}""");
        var map = ReferenceMap.Read("""
        {"game":"Test","labels":[{"file":"jobs.json","list":"/jobs","field":"code"}],
         "references":[{"file":"jobs.json","at":"/jobs/*/ware","target":{"file":"wares.json","list":"/wares","key":"name"},"evidence":"test"}]}
        """);
        var w = new DataWorkspace(DataProject.Open(Data, Path.Combine(root, "schemas")), map: map);
        Assert.True(w.Show("jobs.json"));
        Assert.Equal("J2", w.Window!.Editor.Outline.Label(w.Window.Editor.Session.Current, "/jobs/1"));   // the map's label field
        var jobs = w.TableOf("jobs.json")!;
        Assert.Equal(new ReferenceTarget("wares.json", "/wares/0"), jobs.LinkOf("/jobs/0", "ware"));
        Assert.Null(jobs.LinkOf("/jobs/1", "ware"));   // Silk isn't a ware: nothing to follow
        Assert.Null(jobs.LinkOf("/jobs/0", "crew"));   // not in the map: never guessed
        w.Window.Editor.Select("/jobs/0");
        Assert.True(jobs.Follow("/jobs/0", "ware"));
        Assert.Equal("wares.json", w.Active!.Name);
        Assert.Equal("/wares/0", w.Window!.Editor.Primary);
        Assert.True(w.TableOf("wares.json")!.Back());   // its own history is empty, so back across files
        Assert.Equal("jobs.json", w.Active!.Name);
        Assert.Equal("/jobs/0", w.Window!.Editor.Primary);
        Assert.False(w.Back());
    }

    [Fact]
    public void A_link_to_something_nested_selects_the_item_holding_it_and_says_where_inside()
    {
        File.WriteAllText(Path.Combine(root, "schemas", "map.schema.json"), "{}");
        File.WriteAllText(Path.Combine(root, "schemas", "trips.schema.json"), "{}");
        File.WriteAllText(Path.Combine(Data, "map.json"), """{"sectors":[{"id":"north","stations":[{"id":"dock"}]},{"id":"south","stations":[{"id":"dock"},{"id":"yard"}]}]}""");
        File.WriteAllText(Path.Combine(Data, "trips.json"), """{"trips":[{"sector":"south","station":"yard"}]}""");
        var map = ReferenceMap.Read("""
        {"game":"Test","labels":[],"references":[{"file":"trips.json","at":"/trips/*/station","target":{"file":"map.json","list":"/sectors/*/stations","key":"id"},
          "narrow":{"by":"1/sector","file":"map.json","list":"/sectors","key":"id"},"evidence":"test"}]}
        """);
        var w = new DataWorkspace(DataProject.Open(Data, Path.Combine(root, "schemas")), map: map);
        Assert.True(w.Show("trips.json"));
        Assert.True(w.GoTo(new ReferenceTarget("map.json", "/sectors/1/stations/1")));
        Assert.Equal("/sectors/1", w.Window!.Editor.Primary);   // south, the nearest item the outline knows
        Assert.Contains("inside it, at /stations/1", w.Said);
    }

    [Fact]
    public void Referenced_by_lists_what_links_to_the_selected_item_follows_drafts_and_goes_there()
    {
        File.WriteAllText(Path.Combine(root, "schemas", "jobs.schema.json"), "{}");
        File.WriteAllText(Path.Combine(Data, "jobs.json"), """{"jobs":[{"code":"J1","ware":"Rope"},{"code":"J2","ware":"Silk"},{"code":"J3","ware":"Rope"}]}""");
        var map = ReferenceMap.Read("""
        {"game":"Test","labels":[{"file":"jobs.json","list":"/jobs","field":"code"}],
         "references":[{"file":"jobs.json","at":"/jobs/*/ware","target":{"file":"wares.json","list":"/wares","key":"name"},"evidence":"test"}]}
        """);
        var w = new DataWorkspace(DataProject.Open(Data, Path.Combine(root, "schemas")), map: map);
        Assert.True(w.Show("wares.json"));
        Assert.True(w.Window!.Editor.IsOpen("project.references"));
        Assert.Empty(w.ReferencedBy());   // nothing selected
        w.Window.Editor.Select("/wares/0");
        Assert.Equal(["J1", "J3"], w.ReferencedBy().Select(r => r.Label));
        Assert.Equal(new ReferenceTarget("jobs.json", "/jobs/2/ware"), w.ReferencedBy()[1].At);

        // An unsaved edit in another file counts at once.
        Assert.True(w.Show("jobs.json"));
        Assert.True(w.Window!.Editor.Session.Execute(new SetValue("/jobs/1/ware", Json("\"Rope\""))));
        Assert.True(w.Show("wares.json"));
        Assert.Equal(["J1", "J2", "J3"], w.ReferencedBy().Select(r => r.Label));

        // Choosing one goes there, and Back returns.
        Assert.True(w.GoTo(w.ReferencedBy()[0].At));
        Assert.Equal("jobs.json", w.Active!.Name);
        Assert.Equal("/jobs/0", w.Window!.Editor.Primary);
        Assert.True(w.Back());
        Assert.Equal("wares.json", w.Active!.Name);

        using var host = new HeadlessHost(1400, 800);
        EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        host.Frames(w.Draw, 4);   // the panel draws, docked right
        Assert.True(host.Covered(0, 0, 1400, 800) > 2000);
    }

    [Fact]
    public void Without_a_map_there_is_no_referenced_by_panel()
    {
        var w = Open();
        Assert.True(w.Show("wares.json"));
        Assert.False(w.Window!.Editor.IsOpen("project.references"));
        w.Window.Editor.Select("/wares/0");
        Assert.Empty(w.ReferencedBy());
    }

    [Fact]
    public void A_link_that_cant_be_followed_says_the_resolvers_reason_and_what_to_do()
    {
        // A049's walkthrough: two items sharing an id were refused as "doesn't name an item here", losing why.
        File.WriteAllText(Path.Combine(root, "schemas", "jobs.schema.json"), "{}");
        File.WriteAllText(Path.Combine(Data, "wares.json"), """{"wares":[{"name":"Fern","price":2},{"name":"Fern","price":3}]}""");
        File.WriteAllText(Path.Combine(Data, "jobs.json"), """{"jobs":[{"ware":"Fern","crew":"Ana"},{"ware":"Silk"}]}""");
        var map = ReferenceMap.Read("""
        {"game":"Test","labels":[],
         "references":[{"file":"jobs.json","at":"/jobs/*/ware","target":{"file":"wares.json","list":"/wares","key":"name"},"evidence":"test"}]}
        """);
        var w = new DataWorkspace(DataProject.Open(Data, Path.Combine(root, "schemas")), map: map);
        Assert.True(w.Show("jobs.json"));
        var jobs = w.TableOf("jobs.json")!;
        Assert.False(jobs.Follow("/jobs/0", "ware"));
        Assert.Contains("2 items have the id 'Fern'", jobs.Said);
        Assert.Contains("different id", jobs.Said);
        Assert.False(jobs.Follow("/jobs/1", "ware"));
        Assert.Contains("check its spelling", jobs.Said);
        Assert.False(jobs.Follow("/jobs/0", "crew"));   // not in the map: no reason invented
        Assert.Equal("crew doesn't name an item here.", jobs.Said);
        Assert.Equal("jobs.json", w.Active!.Name);
    }
}
