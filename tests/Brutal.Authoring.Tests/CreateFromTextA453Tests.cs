using Brutal.Authoring.Data;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class CreateFromTextA453Tests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "copy-text-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(root, "data");
    private string Schemas => Path.Combine(root, "schemas");
    private const string Valid = "{\r\n  \"count\": 2\r\n}\r\n";

    public CreateFromTextA453Tests()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Schemas);
        File.WriteAllText(Path.Combine(Schemas, "items.schema.json"), """{"type":"object","required":["count"],"properties":{"count":{"type":"integer","minimum":0}}}""");
    }
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void Preview_checks_exact_text_without_admission_then_copy_preserves_source_and_text()
    {
        var source = Path.Combine(root, "source.json");
        File.WriteAllText(source, Valid);
        var p = DataProject.Open(Data, Schemas);
        var checks = 0;
        p.FileCheck = c => { Assert.Equal(new FileChange(FileChangeKind.Create, "items.json", null, Valid), c); checks++; return null; };
        Assert.True(p.CreateFromText("items.json", File.ReadAllText(source), out var why, preview: true), why);
        Assert.Empty(p.Files);
        Assert.Empty(Directory.GetFiles(Data));
        Assert.True(p.CreateFromText("items.json", File.ReadAllText(source), out why), why);
        Assert.Equal(2, checks);
        Assert.Equal(Valid, File.ReadAllText(source));
        Assert.Equal(Valid, File.ReadAllText(Path.Combine(Data, "items.json")));
        Assert.NotNull(p.Find("items.json")!.Session);
        Assert.Empty(p.Dirty);
        Assert.False(p.CreateFromText("items.json", "{}", out why));
        Assert.Contains("already", why);
        Assert.Equal(Valid, File.ReadAllText(Path.Combine(Data, "items.json")));
    }

    [Theory]
    [InlineData("items.json", "{", "read")]
    [InlineData("items.json", "{}", "count")]
    [InlineData("items.json", "{\"count\":-1}", "must be at least 0")]
    [InlineData("unknown.json", "{}", "schema")]
    [InlineData("../items.json", "{}", "plain")]
    public void Invalid_copies_refuse_in_preview_and_execution(string name, string text, string reason)
    {
        var p = DataProject.Open(Data, Schemas);
        foreach (var preview in new[] { true, false })
        {
            Assert.False(p.CreateFromText(name, text, out var why, preview));
            Assert.Contains(reason, why, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(p.Files);
            Assert.Empty(Directory.GetFiles(Data));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Global_and_per_file_read_only_are_preserved(bool global)
    {
        var p = DataProject.Open(Data, Schemas, readOnly: global, readOnlyFile: _ => global ? null : "generated elsewhere");
        Assert.False(p.CreateFromText("items.json", Valid, out var why));
        Assert.Contains(global ? "read-only" : "generated elsewhere", why);
        Assert.Empty(Directory.GetFiles(Data));
    }

    [Fact]
    public void Host_schema_and_rules_are_used_without_rewriting_text()
    {
        var p = DataProject.Open(Data, Schemas, rules: _ => new Reject());
        p.SchemaFor = _ => "items.schema.json";
        Assert.False(p.CreateFromText("other.json", Valid, out var why));
        Assert.Contains("copy rule", why);
        Assert.Empty(Directory.GetFiles(Data));
        var allowed = DataProject.Open(Data, Schemas);
        allowed.SchemaFor = _ => "items.schema.json";
        Assert.True(allowed.CreateFromText("other.json", Valid, out why), why);
        Assert.Equal("items.schema.json", allowed.Find("other.json")!.Schema);
        Assert.Equal(Valid, File.ReadAllText(Path.Combine(Data, "other.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Whole_file_refusal_or_exception_never_writes(bool throws)
    {
        var p = DataProject.Open(Data, Schemas);
        p.FileCheck = _ => throws ? throw new InvalidOperationException("check fault") : "dependent file refuses";
        Assert.False(p.CreateFromText("items.json", Valid, out var why, preview: true));
        Assert.Contains(throws ? "check fault" : "dependent file refuses", why);
        Assert.False(p.CreateFromText("items.json", Valid, out why));
        Assert.Empty(p.Files);
        Assert.Empty(Directory.GetFiles(Data));
    }

    [Fact]
    public void Callback_name_race_is_not_overwritten_or_admitted()
    {
        var p = DataProject.Open(Data, Schemas);
        p.FileCheck = _ => { File.WriteAllText(Path.Combine(Data, "items.json"), "competing writer"); return null; };
        Assert.False(p.CreateFromText("items.json", Valid, out var why));
        Assert.Contains("already", why);
        Assert.Equal("competing writer", File.ReadAllText(Path.Combine(Data, "items.json")));
        Assert.Empty(p.Files);
    }

    [Fact]
    public void Callback_read_only_change_is_rechecked()
    {
        var locked = false;
        var p = DataProject.Open(Data, Schemas, readOnlyFile: _ => locked ? "now locked" : null);
        p.FileCheck = _ => { locked = true; return null; };
        Assert.False(p.CreateFromText("items.json", Valid, out var why));
        Assert.Contains("now locked", why);
        Assert.Empty(Directory.GetFiles(Data));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Faulting_schema_or_rules_refuse_without_admitting(bool schemaFault)
    {
        var p = DataProject.Open(Data, Schemas, rules: _ => throw new InvalidOperationException("rules fault"));
        if (schemaFault) p.SchemaFor = _ => throw new InvalidOperationException("schema fault");
        Assert.False(p.CreateFromText("items.json", Valid, out var why));
        Assert.Contains(schemaFault ? "schema fault" : "rules fault", why);
        Assert.Empty(p.Files);
        Assert.Empty(Directory.GetFiles(Data));
    }

    private sealed class Reject : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument document) => [Issue.Error("copy rule")];
    }
}
