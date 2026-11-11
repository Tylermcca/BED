using System.Text.Json;
using Bed.Data;
using Xunit;

namespace Bed.Tests;

/// <summary>A data folder and a schema folder opened as one project (A038's piece P).</summary>
public sealed class DataProjectTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "dataproject-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");

    public DataProjectTests()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Schemas);
        File.WriteAllText(Path.Combine(Schemas, "wares.schema.json"), """{"type":"object","properties":{"wares":{"type":"array","items":{"type":"object","properties":{"price":{"type":"number","minimum":0}}}}}}""");
        File.WriteAllText(Path.Combine(Schemas, "shared.schema.json"), """{"type":"object","required":["name"]}""");
        File.WriteAllText(Path.Combine(Data, "wares.json"), "{\n  \"wares\": [ { \"price\": 2 } ]\n}\n");
        File.WriteAllText(Path.Combine(Data, "crew.json"), """{"$schema":"../schemas/shared.schema.json","name":"crew"}""");
        File.WriteAllText(Path.Combine(Data, "broken.json"), "{ not json");
        File.WriteAllText(Path.Combine(Data, "orphan.json"), "{}");
        File.WriteAllText(Path.Combine(Data, "bad.json"), """{"$schema":"shared.schema.json"}""");
        File.WriteAllText(Path.Combine(Data, "notes.txt"), "not data");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    /// <summary>Data beside its schemas in one folder (A212: a game's data/specials.json beside specials.schema.json): the schema isn't
    /// listed as data, it's still found as the data's schema, and it's the only kind offered for a new file.</summary>
    [Fact]
    public void In_one_shared_folder_a_schema_is_a_schema_not_data()
    {
        var shared = Path.Combine(root, "shared");
        Directory.CreateDirectory(shared);
        File.WriteAllText(Path.Combine(shared, "wares.schema.json"), """{"type":"object","properties":{"wares":{"type":"array"}}}""");
        File.WriteAllText(Path.Combine(shared, "wares.json"), """{"wares":[]}""");
        File.WriteAllText(Path.Combine(shared, "notes.json"), "{}");
        var p = DataProject.Open(shared, shared);
        Assert.True(p.SharedFolder);
        Assert.Equal("notes.json wares.json", string.Join(" ", p.Files.Select(f => f.Name)));
        Assert.Equal(["wares.schema.json"], p.SchemaNames);
        var wares = p.Files.Single(f => f.Name == "wares.json");
        Assert.True(wares.Open(), string.Join(" ", wares.Problems));
        Assert.False(DataProject.Open(Data, Schemas).SharedFolder);   // separate folders list as before
    }

    /// <summary>A218's F3: broken data beside its schema in one folder used to stop the whole project opening, because every
    /// *.json there was read as a schema. Now the project opens and only that file is refused, with why.</summary>
    [Fact]
    public void In_one_shared_folder_broken_data_is_one_refused_file_not_a_project_that_cant_open()
    {
        var shared = Path.Combine(root, "shared");
        Directory.CreateDirectory(shared);
        File.WriteAllText(Path.Combine(shared, "wares.schema.json"), """{"type":"object","properties":{"wares":{"type":"array"}}}""");
        File.WriteAllText(Path.Combine(shared, "wares.json"), """{"wares":[""");
        File.WriteAllText(Path.Combine(shared, "crew.schema.json"), """{"type":"object"}""");
        File.WriteAllText(Path.Combine(shared, "crew.json"), "{}");
        var p = DataProject.Open(shared, shared);
        var wares = p.Find("wares.json")!;
        Assert.False(wares.Open());
        Assert.Contains("isn't valid JSON", wares.Problems[0].Message);
        Assert.Equal(DataFileState.Refused, wares.State);
        Assert.True(p.Find("crew.json")!.Open());
        Assert.Equal("""{"wares":[""", File.ReadAllText(wares.Path));   // nothing is written
    }

    [Fact]
    public void Every_json_file_is_listed_in_order_and_none_is_read_yet()
    {
        var p = DataProject.Open(Data, Schemas);
        Assert.Equal("bad.json broken.json crew.json orphan.json wares.json", string.Join(" ", p.Files.Select(f => f.Name)));
        Assert.All(p.Files, f => Assert.Equal(DataFileState.NotOpened, f.State));
        Assert.Empty(p.Dirty);
    }

    [Fact]
    public void A_file_opens_against_its_schema_by_name_or_by_its_schema_field()
    {
        var p = DataProject.Open(Data, Schemas);
        Assert.True(p.Find("wares.json")!.Open());
        Assert.Equal("wares.schema.json", p.Find("wares.json")!.Schema);
        Assert.True(p.Find("crew.json")!.Open());
        Assert.Equal("shared.schema.json", p.Find("crew.json")!.Schema);
        Assert.Equal(DataFileState.Open, p.Find("crew.json")!.State);
    }

    [Fact]
    public void A_file_that_cant_open_says_why_and_the_others_still_open()
    {
        var p = DataProject.Open(Data, Schemas);
        Assert.False(p.Find("broken.json")!.Open());
        Assert.Contains("isn't valid JSON", p.Find("broken.json")!.Problems[0].Message);
        Assert.False(p.Find("orphan.json")!.Open());
        Assert.Contains("orphan.schema.json", p.Find("orphan.json")!.Problems[0].Message);
        Assert.False(p.Find("bad.json")!.Open());
        Assert.Contains(p.Find("bad.json")!.Problems, i => i.Message.Contains("'name' is required"));
        Assert.All(new[] { "broken.json", "orphan.json", "bad.json" }, n => Assert.Equal(DataFileState.Refused, p.Find(n)!.State));
        Assert.True(p.Find("wares.json")!.Open());
    }

    [Fact]
    public void Each_file_has_its_own_session_and_save()
    {
        var p = DataProject.Open(Data, Schemas);
        var wares = p.Find("wares.json")!;
        Assert.True(wares.Open());
        Assert.True(wares.Session!.Execute(new SetValue("/wares/0/price", Json("5"))));
        Assert.Equal([wares], p.Dirty);
        Assert.Empty(p.SaveAll());
        Assert.Empty(p.Dirty);
        Assert.Equal("{\n  \"wares\": [ { \"price\": 5 } ]\n}\n", File.ReadAllText(wares.Path));   // only the value changed
    }

    [Fact]
    public void Saving_over_an_outside_change_is_refused()
    {
        var p = DataProject.Open(Data, Schemas);
        var wares = p.Find("wares.json")!;
        Assert.True(wares.Open());
        Assert.True(wares.Session!.Execute(new SetValue("/wares/0/price", Json("5"))));
        File.WriteAllText(wares.Path, """{"wares":[{"price":9}]}""");
        Assert.Equal([wares], p.SaveAll());
        Assert.Contains("changed outside the editor", wares.Problems[0].Message);
        Assert.Equal("""{"wares":[{"price":9}]}""", File.ReadAllText(wares.Path));
        Assert.True(wares.Dirty);   // the edit is kept for the person to decide
    }

    [Fact]
    public void A_read_only_project_never_writes()
    {
        var p = DataProject.Open(Data, Schemas, readOnly: true);
        var wares = p.Find("wares.json")!;
        Assert.True(wares.Open());
        Assert.True(wares.Session!.Execute(new SetValue("/wares/0/price", Json("5"))));
        Assert.False(wares.Save());
        Assert.Contains("read-only", wares.Problems[0].Message);
        Assert.Contains("\"price\": 2", File.ReadAllText(wares.Path));
    }

    [Fact]
    public void Refresh_adds_new_files_and_keeps_a_vanished_ones_edits()
    {
        var p = DataProject.Open(Data, Schemas);
        var wares = p.Find("wares.json")!;
        Assert.True(wares.Open());
        Assert.True(wares.Session!.Execute(new SetValue("/wares/0/price", Json("5"))));
        File.Delete(wares.Path);
        File.WriteAllText(Path.Combine(Data, "added.json"), "{}");
        p.Refresh();
        Assert.Equal(DataFileState.Missing, wares.State);
        Assert.True(wares.Dirty);
        Assert.NotNull(p.Find("added.json"));
        File.WriteAllText(wares.Path, "{\n  \"wares\": [ { \"price\": 2 } ]\n}\n");
        p.Refresh();
        Assert.Equal(DataFileState.Open, wares.State);
    }

    [Fact]
    public void Closing_drops_the_session_and_reopening_reads_the_file_again()
    {
        var p = DataProject.Open(Data, Schemas);
        var wares = p.Find("wares.json")!;
        Assert.True(wares.Open());
        File.WriteAllText(wares.Path, """{"wares":[{"price":7}]}""");
        wares.Close();
        Assert.Equal(DataFileState.NotOpened, wares.State);
        Assert.True(wares.Open());
        Assert.Equal(7, wares.Session!.Current.At("/wares/0/price")!.Value.GetDouble());
    }

    [Fact]
    public void Missing_folders_are_refused_by_name()
    {
        var e = Assert.Throws<DirectoryNotFoundException>(() => DataProject.Open(Path.Combine(root, "nope"), Schemas));
        Assert.Contains("data folder", e.Message);
    }

    [Fact]
    public void A_game_can_mark_single_files_read_only_and_the_rest_still_save()
    {
        var p = DataProject.Open(Data, Schemas, readOnlyFile: name => name == "wares.json" ? "a spreadsheet exports it" : null);
        var wares = p.Find("wares.json")!;
        Assert.Equal("a spreadsheet exports it", wares.ReadOnlyReason);
        Assert.Null(p.Find("crew.json")!.ReadOnlyReason);
        Assert.True(wares.Open());
        Assert.True(wares.Session!.Execute(new SetValue("/wares/0/price", Json("5"))));
        Assert.False(wares.Save());
        Assert.Equal("wares.json wasn't saved: a spreadsheet exports it", wares.Problems[0].Message);
        Assert.Contains("\"price\": 2", File.ReadAllText(wares.Path));
        Assert.Equal("the project is open read-only", DataProject.Open(Data, Schemas, readOnly: true).Find("crew.json")!.ReadOnlyReason);   // the project's rule wins
    }

    // A game's own rule: no ware may cost more than 10, which the schema doesn't say.
    private sealed class PriceCap : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument d) =>
            d.At("/wares/0/price") is { ValueKind: JsonValueKind.Number } p && p.GetDouble() > 10 ? [Issue.Error("the game caps prices at 10", null, "$.wares[0].price")] : [];
    }

    [Fact]
    public void A_games_own_rules_refuse_edits_the_schema_allows()
    {
        var p = DataProject.Open(Data, Schemas, rules: name => name == "wares.json" ? new PriceCap() : null);
        var wares = p.Find("wares.json")!;
        Assert.True(wares.Open());
        Assert.True(wares.Session!.Execute(new SetValue("/wares/0/price", Json("9"))));
        Assert.False(wares.Session.Execute(new SetValue("/wares/0/price", Json("11"))));   // the schema allows it; the game doesn't
        Assert.Contains(wares.Session.LastIssues, i => i.Message == "the game caps prices at 10");
        Assert.Equal(9, wares.Session.Current.At("/wares/0/price")!.Value.GetDouble());
        Assert.True(p.Find("crew.json")!.Open());   // other files have no extra rules
    }

    [Fact]
    public void A_file_breaking_the_games_rules_on_disk_says_so_and_stays_closed()
    {
        File.WriteAllText(Path.Combine(Data, "wares.json"), """{"wares":[{"price":50}]}""");
        var wares = DataProject.Open(Data, Schemas, rules: name => name == "wares.json" ? new PriceCap() : null).Find("wares.json")!;
        Assert.False(wares.Open());
        Assert.Contains(wares.Problems, i => i.Message == "the game caps prices at 10");
    }

    [Fact]
    public void A_file_deleted_outside_is_never_silently_put_back()   // found by A049
    {
        var p = DataProject.Open(Data, Schemas);
        var wares = p.Find("wares.json")!;
        Assert.True(wares.Open());
        Assert.True(wares.Session!.Execute(new SetValue("/wares/0/price", Json("5"))));
        File.Delete(wares.Path);
        Assert.False(wares.Save());
        Assert.Contains("deleted outside", wares.Problems[0].Message);
        Assert.False(File.Exists(wares.Path));
        Assert.True(wares.Dirty);   // the edits are kept for Save as
    }
}
