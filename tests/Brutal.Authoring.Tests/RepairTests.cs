using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.ImGui;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>A file that doesn't fit its schema can be repaired in the editor: its problems are listed, and an edit is
/// accepted when it adds no problem of its own, until none are left.</summary>
public sealed class RepairTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "repair-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");

    public RepairTests()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Schemas);
        File.WriteAllText(Path.Combine(Schemas, "wares.schema.json"), """
        {"type":"object","properties":{"wares":{"type":"array","items":{"type":"object","required":["id"],
          "properties":{"id":{"type":"string"},"price":{"type":"number","minimum":0}}}}}}
        """);
        File.WriteAllText(Path.Combine(Data, "wares.json"), "{\n  \"wares\": [\n    { \"id\": \"ore\", \"price\": -5 },\n    { \"price\": 3 }\n  ]\n}\n");
        File.WriteAllText(Path.Combine(Data, "notjson.json"), "{ nope");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static JsonElement J(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private DataFile Wares() => DataProject.Open(Data, Schemas).Find("wares.json")!;

    [Fact]
    public void A_broken_file_opens_for_repair_with_its_problems_listed()
    {
        var f = Wares();
        Assert.False(f.Open());   // ordinary opening still refuses it
        Assert.True(f.Repair());
        Assert.Equal(DataFileState.Repairing, f.State);
        Assert.Equal(2, f.Session!.Problems.Count);
    }

    [Fact]
    public void Fixing_problems_one_at_a_time_is_allowed_and_the_last_fix_makes_it_ordinary()
    {
        var f = Wares();
        Assert.True(f.Repair());
        Assert.True(f.Session!.Execute(new SetValue("/wares/0/price", J("5"))));
        Assert.Single(f.Session.Problems);
        Assert.True(f.Session.Repairing);
        Assert.True(f.Session.Execute(new SetValue("/wares/1/id", J("\"salt\""))));
        Assert.False(f.Session.Repairing);
        Assert.Equal(DataFileState.Open, f.State);
        Assert.False(f.Session.Execute(new SetValue("/wares/0/price", J("-1"))));   // ordinary again: no problems allowed
    }

    [Fact]
    public void An_edit_that_adds_a_problem_is_refused_even_while_repairing()
    {
        var f = Wares();
        Assert.True(f.Repair());
        var before = f.Session!.Current;
        Assert.False(f.Session.Execute(new SetValue("/wares/0/id", J("7"))));   // a new problem, elsewhere
        Assert.Single(f.Session.LastIssues);   // only the one it would add is named
        Assert.Same(before, f.Session.Current);
        Assert.True(f.Session.Execute(new SetValue("/wares/0/price", J("-9"))));   // the same problem, still there, is no worse
    }

    [Fact]
    public void Undo_back_into_a_broken_state_repairs_again_and_save_writes_the_fixes()
    {
        var f = Wares();
        Assert.True(f.Repair());
        Assert.True(f.Session!.Execute(new SetValue("/wares/0/price", J("5"))));
        Assert.True(f.Session.Execute(new SetValue("/wares/1/id", J("\"salt\""))));
        Assert.False(f.Session.Repairing);
        Assert.True(f.Session.Undo());
        Assert.True(f.Session.Repairing);
        Assert.True(f.Session.Redo());
        Assert.True(f.Save());
        Assert.True(DataProject.Open(Data, Schemas).Find("wares.json")!.Open());   // now valid on disk
        Assert.Contains("\"price\": 5", File.ReadAllText(Path.Combine(Data, "wares.json")));
    }

    [Fact]
    public void Text_that_isnt_json_can_only_be_fixed_outside()
    {
        var f = DataProject.Open(Data, Schemas).Find("notjson.json")!;
        Assert.False(f.Repair());
        Assert.Contains("isn't valid JSON", f.Problems[0].Message);
    }

    [Fact]
    public void The_workspace_repairs_from_the_files_panel()
    {
        var w = new DataWorkspace(DataProject.Open(Data, Schemas));
        Assert.False(w.Show("wares.json"));
        Assert.True(w.Repair("wares.json"));
        Assert.Contains("2 problem(s) to fix", w.Said);
        Assert.True(w.Window!.Editor.Session.Execute(new SetValue("/wares/0/price", J("5"))));
        Assert.False(w.Repair("notjson.json"));
    }

    [Fact]
    public void A_file_with_windows_line_endings_keeps_them_on_every_line()   // found by A048: they came back LF
    {
        var path = Path.Combine(Data, "wares.json");
        File.WriteAllText(path, "{\r\n  \"wares\": [\r\n    { \"id\": \"ore\", \"price\": 2 },\r\n    { \"id\": \"salt\", \"price\": 3 }\r\n  ]\r\n}\r\n");
        var f = DataProject.Open(Data, Schemas).Find("wares.json")!;
        Assert.True(f.Open());
        Assert.False(f.Dirty);   // reading and writing back unchanged changes nothing
        Assert.True(f.Session!.Execute(new SetValue("/wares/0/price", J("5"))));
        Assert.True(f.Save());
        Assert.Equal("{\r\n  \"wares\": [\r\n    { \"id\": \"ore\", \"price\": 5 },\r\n    { \"id\": \"salt\", \"price\": 3 }\r\n  ]\r\n}\r\n", File.ReadAllText(path));
    }
}
