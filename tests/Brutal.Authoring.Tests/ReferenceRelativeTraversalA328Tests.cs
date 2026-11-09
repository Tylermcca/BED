using System.Text.Json;
using Brutal.Authoring.Data;
using Xunit;

namespace Brutal.Authoring.Tests;

// INVENTED tiny fixtures, exercised through the existing public resolver, never private-method reflection.
public sealed class ReferenceRelativeTraversalA328Tests : IDisposable
{
    private readonly string owner = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "reference-relative-a328"));
    private readonly string root;

    public ReferenceRelativeTraversalA328Tests()
    {
        root = Path.Combine(owner, Guid.NewGuid().ToString("N"));
    }

    [Theory]
    [InlineData("{\"rows\":[{\"ref\":\"pick\"}]}", "/rows/0/ref", "1#", ReferenceOutcome.Found, "/items/0")]
    [InlineData("{\"rows\":{\"0\":{\"ref\":\"pick\"}}}", "/rows/0/ref", "1#", ReferenceOutcome.Found, "/items/1")]
    [InlineData("{\"rows\":[{\"ref\":\"pick\",\"a/b\":\"slash\"}]}", "/rows/0/ref", "1/a~1b", ReferenceOutcome.Found, "/items/2")]
    [InlineData("{\"rows\":[{\"ref\":\"pick\"}]}", "/rows/0/ref", "9/missing", ReferenceOutcome.NeedsScope, null)]
    [InlineData("{\"rows\":[{\"ref\":\"pick\"}]}", "/rows/0/ref", "1/missing", ReferenceOutcome.NeedsScope, null)]
    public void Relative_source_navigation_preserves_types_escapes_and_missing_scope(
        string source, string pointer, string relative, ReferenceOutcome outcome, string? target)
    {
        var data = Path.Combine(root, "data");
        var schemas = Path.Combine(root, "schemas");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(schemas);
        File.WriteAllText(Path.Combine(data, "source.json"), source);
        File.WriteAllText(Path.Combine(data, "target.json"),
            """{"items":[{"id":"pick","tag":0},{"id":"pick","tag":"0"},{"id":"pick","tag":"slash"}]}""");
        File.WriteAllText(Path.Combine(schemas, "source.schema.json"), "{}");
        File.WriteAllText(Path.Combine(schemas, "target.schema.json"), "{}");
        var map = ReferenceMap.Read($$$"""
            {"game":"A328 synthetic","labels":[],"references":[
              {"file":"source.json","at":"/rows/*/ref","evidence":"synthetic typed join",
               "target":{"file":"target.json","list":"/items","key":"id",
                 "where":[{"at":"0/tag","equalsSource":{{{JsonSerializer.Serialize(relative)}}} }]}}]}
            """);
        var project = DataProject.Open(data, schemas);
        var file = project.Find("source.json")!;
        Assert.True(file.Open());
        var result = map.Resolve(project, "source.json", pointer, file.Session!.Current.At(pointer)!.Value);
        Assert.Equal(outcome, result.Outcome);
        if (target is null)
        {
            Assert.Null(result.Target);
            Assert.Contains(relative, result.Note);
        }
        else
        {
            Assert.Equal(new ReferenceTarget("target.json", target), result.Target);
            Assert.Null(result.Note);
        }
    }

    public void Dispose()
    {
        var resolved = Path.GetFullPath(root);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(resolved), owner)
            || !Guid.TryParseExact(Path.GetFileName(resolved), "N", out _))
            throw new InvalidOperationException("Refusing cleanup outside this fixture's owned GUID child.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }
}
