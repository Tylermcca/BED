using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;

namespace Bed.Tests;

/// <summary>Making, renaming, deleting and restoring a project's data files (the user's gap list, item 5), in the project
/// and in the editor's Files panel and palette.</summary>
[Collection(ImGuiCollection.Name)]
public sealed class DataFileOperationsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "datafileops-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");

    public DataFileOperationsTests()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Schemas);
        File.WriteAllText(Path.Combine(Schemas, "wares.schema.json"),
            """{"type":"object","required":["wares","version"],"properties":{"wares":{"type":"array","items":{"type":"object"}},"version":{"type":"integer","default":1}}}""");
        File.WriteAllText(Path.Combine(Schemas, "crew.schema.json"),
            """{"type":"object","required":["crew"],"properties":{"crew":{"type":"array","minItems":1}}}""");
        File.WriteAllText(Path.Combine(Data, "wares.json"), "{\"wares\":[{\"name\":\"Rope\"}],\"version\":1}\r\n");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private DataProject Open(bool readOnly = false, Func<string, string?>? readOnlyFile = null) => DataProject.Open(Data, Schemas, readOnly, readOnlyFile);

    private JsonElement Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, name))).RootElement;

    [Fact]
    public void A_new_file_holds_what_its_schema_requires_and_opens()
    {
        var p = Open();
        Assert.Equal(["crew.schema.json", "wares.schema.json"], p.SchemaNames);
        Assert.True(p.Create("spare_wares.json", "wares.schema.json", out var why), why);
        var made = Read("spare_wares.json");
        Assert.Equal("wares.schema.json", made.GetProperty("$schema").GetString());   // its name alone wouldn't pick the schema
        Assert.Equal(0, made.GetProperty("wares").GetArrayLength());
        Assert.Equal(1, made.GetProperty("version").GetInt32());   // the schema's default
        Assert.Equal(DataFileState.Open, p.Find("spare_wares.json")!.State);
        Assert.EndsWith("\n", File.ReadAllText(Path.Combine(Data, "spare_wares.json")));
    }

    [Fact]
    public void A_new_file_named_for_its_schema_needs_no_schema_field()
    {
        Directory.Delete(Data, true);
        Directory.CreateDirectory(Data);
        var p = Open();
        Assert.True(p.Create("wares.json", "wares.schema.json", out _));
        Assert.False(Read("wares.json").TryGetProperty("$schema", out _));
    }

    [Theory]
    [InlineData("wares.json", "wares.schema.json", "already a file called wares.json")]
    [InlineData("../out.json", "wares.schema.json", "isn't a plain file name")]
    [InlineData("notes.txt", "wares.schema.json", "isn't a plain file name")]
    [InlineData("", "wares.schema.json", "isn't a plain file name")]
    [InlineData("more.json", "ships.schema.json", "no schema called ships.schema.json")]
    [InlineData("crew.json", "crew.schema.json", "would start invalid")]   // minItems 1: an empty list won't do
    public void A_new_file_is_refused_with_the_reason(string name, string schema, string reason)
    {
        var p = Open();
        var before = Directory.GetFiles(Data).Length;
        Assert.False(p.Create(name, schema, out var why));
        Assert.Contains(reason, why);
        Assert.Equal(before, Directory.GetFiles(Data).Length);
        Assert.False(File.Exists(Path.Combine(root, "out.json")));
    }

    [Fact]
    public void Read_only_projects_and_files_refuse_every_change_to_the_folder()
    {
        var p = Open(readOnly: true);
        Assert.False(p.Create("new.json", "wares.schema.json", out var why));
        Assert.Contains("read-only", why);
        Assert.False(p.Rename("wares.json", "goods.json", out _));
        Assert.False(p.Delete("wares.json", out _));
        var q = Open(readOnlyFile: n => n == "wares.json" ? "another tool writes it" : null);
        Assert.False(q.Delete("wares.json", out why));
        Assert.Contains("another tool writes it", why);
        Assert.False(q.Rename("wares.json", "goods.json", out _));
        Assert.True(File.Exists(Path.Combine(Data, "wares.json")));
    }

    [Fact]
    public void Rename_keeps_the_bytes_reopens_it_and_refuses_a_schema_change()
    {
        var p = Open();
        Assert.True(p.Find("wares.json")!.Open());
        Assert.False(p.Rename("wares.json", "goods.json", out var why));   // goods.schema.json would be its schema
        Assert.Contains("add \"$schema\": \"wares.schema.json\"", why);
        File.WriteAllText(Path.Combine(Data, "wares.json"), "{\"$schema\":\"wares.schema.json\",\"wares\":[],\"version\":1}\r\n");
        var bytes = File.ReadAllBytes(Path.Combine(Data, "wares.json"));
        var q = Open();
        Assert.True(q.Find("wares.json")!.Open());
        Assert.True(q.Rename("wares.json", "goods.json", out why), why);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(Data, "goods.json")));
        Assert.False(File.Exists(Path.Combine(Data, "wares.json")));
        Assert.Null(q.Find("wares.json"));
        Assert.Equal(DataFileState.Open, q.Find("goods.json")!.State);
    }

    [Fact]
    public void Rename_and_delete_refuse_unsaved_edits_and_taken_names()
    {
        var p = Open();
        File.WriteAllText(Path.Combine(Data, "spare.json"), "{\"$schema\":\"wares.schema.json\",\"wares\":[],\"version\":1}");
        p.Refresh();
        var wares = p.Find("wares.json")!;
        Assert.True(wares.Open());
        Assert.True(wares.Session!.Execute(new SetValue("/version", JsonDocument.Parse("2").RootElement)));
        Assert.False(p.Rename("wares.json", "x.json", out var why));
        Assert.Contains("unsaved edits", why);
        Assert.False(p.Delete("wares.json", out why));
        Assert.Contains("unsaved edits", why);
        Assert.False(p.Rename("spare.json", "wares.json", out why));
        Assert.Contains("already a file", why);
        Assert.False(p.Delete("nothing.json", out why));
        Assert.Contains("no file called nothing.json", why);
    }

    [Fact]
    public void Delete_then_restore_gives_back_the_same_bytes_unless_the_name_is_taken_again()
    {
        var p = Open();
        var bytes = File.ReadAllBytes(Path.Combine(Data, "wares.json"));
        Assert.True(p.Delete("wares.json", out _));
        Assert.False(File.Exists(Path.Combine(Data, "wares.json")));
        Assert.Null(p.Find("wares.json"));
        Assert.Equal(["wares.json"], p.Deleted);
        Assert.True(p.Restore("wares.json", out _));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(Data, "wares.json")));
        Assert.True(p.Find("wares.json")!.Open());
        Assert.Empty(p.Deleted);
        Assert.False(p.Restore("wares.json", out var why));
        Assert.Contains("wasn't deleted here", why);

        Assert.True(p.Delete("wares.json", out _));
        File.WriteAllText(Path.Combine(Data, "wares.json"), "{}");   // someone made another meanwhile
        Assert.False(p.Restore("wares.json", out why));
        Assert.Contains("can't be put back", why);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(Data, "wares.json")));
    }

    [Fact]
    public void The_workspace_makes_renames_and_deletes_files_and_says_what_happened()
    {
        var w = new DataWorkspace(Open());
        Assert.True(w.NewFile("spare.json", "wares.schema.json"));
        Assert.Equal("spare.json", w.Active!.Name);
        Assert.Contains("Made spare.json", w.Said);
        Assert.True(w.RenameFile("spare.json", "extra.json"));
        Assert.Equal("extra.json", w.Active!.Name);
        Assert.Equal("extra.json", w.Window!.Editor.Session.FilePath is { } path ? Path.GetFileName(path) : null);
        Assert.True(w.DeleteFile("extra.json"));
        Assert.Null(w.Active);
        Assert.True(w.RestoreFile("extra.json"));
        Assert.True(w.Show("extra.json"));
        Assert.False(w.NewFile("extra.json", "wares.schema.json"));
        Assert.Contains("already a file", w.Said);
    }

    [Fact]
    public void Links_into_a_file_are_counted_before_it_is_renamed_or_deleted()
    {
        File.WriteAllText(Path.Combine(Schemas, "jobs.schema.json"), "{}");
        File.WriteAllText(Path.Combine(Data, "jobs.json"), """{"jobs":[{"ware":"Rope"},{"ware":"Rope"},{"ware":"Silk"}]}""");
        var map = ReferenceMap.Read("""
        {"game":"Test","labels":[],
         "references":[{"file":"jobs.json","at":"/jobs/*/ware","target":{"file":"wares.json","list":"/wares","key":"name"},"evidence":"test"}]}
        """);
        var w = new DataWorkspace(Open(), map: map);
        Assert.Equal(2, w.LinksInto("wares.json"));   // Silk names nothing, so it isn't counted
        Assert.Equal(0, w.LinksInto("jobs.json"));
        Assert.Equal(0, new DataWorkspace(Open()).LinksInto("wares.json"));   // no map, no links known
    }

    [Fact]
    public void The_palette_offers_each_with_a_reason_when_it_cant_and_the_form_draws()
    {
        var w = new DataWorkspace(Open());
        Assert.True(w.Show("wares.json"));
        var window = w.Window!;
        Assert.Null(Assert.Single(window.PaletteEntries("rename the shown file")).Unavailable);
        Assert.Equal("no file was deleted here", Assert.Single(window.PaletteEntries("restore the last deleted file")).Unavailable);
        Assert.True(window.Run(EditorWindow<DataDocument>.PanelCommandAction + "project.files/new_file"));
        using var host = new HeadlessHost(1400, 800);
        EditorWindow<DataDocument>.Configure(Brutal.ImGuiApi.ImGui.GetIO());
        host.Frames(w.Draw, 4);
        Assert.True(host.Covered(0, 0, 1400, 800) > 2000);

        var readOnly = new DataWorkspace(Open(readOnly: true));
        Assert.True(readOnly.Show("wares.json"));
        Assert.Equal("the project is open read-only", Assert.Single(readOnly.Window!.PaletteEntries("new data file")).Unavailable);
    }
}
