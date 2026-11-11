using System.Text.Json;
using Bed.Data;
using Xunit;

namespace Bed.Tests;

public sealed class ReverseLinkCachePerf02Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("own-reverse-cache-").FullName;
    private readonly DataProject project;
    private readonly ReferenceMap map;
    public ReverseLinkCachePerf02Tests()
    {
        var data = Path.Combine(root, "data"); var schemas = Path.Combine(root, "schemas"); Directory.CreateDirectory(data); Directory.CreateDirectory(schemas);
        foreach (var name in new[] { "a", "b", "c", "d" }) File.WriteAllText(Path.Combine(schemas, name + ".schema.json"), "{}");
        File.WriteAllText(Path.Combine(data, "a.json"), """{"refs":[{"target":"one","owner":"red"}],"local":{"group":"left","target":"same"},"keyed":{"red":"one"},"pair":"red/one"}""");
        File.WriteAllText(Path.Combine(data, "b.json"), """{"items":[{"id":"one","owner":"red"},{"id":"one","owner":"blue"}],"groups":[{"id":"left","parts":[{"id":"same"}]},{"id":"right","parts":[{"id":"same"}]}]}""");
        File.WriteAllText(Path.Combine(data, "c.json"), """{"owners":{"red":{},"blue":{}}}""");
        File.WriteAllText(Path.Combine(data, "d.json"), """{"values":["one","two"]}""");
        project = DataProject.Open(data, schemas);
        map = ReferenceMap.Read("""
            {"game":"Own fixture","labels":[],"references":[
             {"file":"a.json","at":"/refs/*/target","target":{"file":"b.json","list":"/items","key":"id","where":[{"at":"0/owner","equalsSource":"1/owner"}]},"evidence":"own"},
             {"file":"a.json","at":"/local/target","target":{"file":"b.json","list":"/groups/*/parts","key":"id"},"narrow":{"by":"1/group","file":"b.json","list":"/groups","key":"id"},"evidence":"own"},
             {"file":"a.json","at":"/keyed","keys":true,"target":{"file":"c.json","list":"/owners","key":"$key"},"evidence":"own"},
             {"file":"a.json","at":"/keyed/*","target":{"file":"d.json","list":"/values","key":"$value"},"evidence":"own"},
             {"file":"a.json","at":"/pair","target":{"file":"d.json","list":"/values","key":"$value"},"parts":{"separator":"/","targets":[{"file":"c.json","list":"/owners","key":"$key"},{"file":"d.json","list":"/values","key":"$value"}]},"evidence":"own"}]}
            """);
    }
    public void Dispose() => Directory.Delete(root, true);
    private ReferenceResult Link(string pointer) => Assert.Single(map.Links(project), l => l.At.Pointer == pointer).Result;
    private void Set(string file, string pointer, string value)
    {
        var f = project.Find(file)!; Assert.True(f.Open()); Assert.True(f.Session!.Execute(new SetValue(pointer, JsonSerializer.SerializeToElement(value))));
    }
    [Fact]
    public void Repeated_reverse_walks_follow_source_conditions_target_edits_undo_and_narrowing()
    {
        Assert.Equal("/items/0", Link("/refs/0/target").Target!.Pointer); Assert.Equal("/items/0", Link("/refs/0/target").Target!.Pointer);
        Set("a.json", "/refs/0/owner", "blue"); Assert.Equal("/items/1", Link("/refs/0/target").Target!.Pointer);
        Set("b.json", "/items/1/id", "two"); Assert.Equal(ReferenceOutcome.Missing, Link("/refs/0/target").Outcome);
        Assert.True(project.Find("b.json")!.Session!.Undo()); Assert.Equal("/items/1", Link("/refs/0/target").Target!.Pointer);
        Assert.Equal("/groups/0/parts/0", Link("/local/target").Target!.Pointer);
        Set("a.json", "/local/group", "right"); Assert.Equal("/groups/1/parts/0", Link("/local/target").Target!.Pointer);
    }
    [Fact]
    public void Key_value_and_split_meanings_remain_separate_in_cached_reverse_results()
    {
        var first = map.Links(project).Where(l => l.At.Pointer == "/keyed/red").Select(l => l.Result.Target).ToArray();
        Assert.Contains(new ReferenceTarget("c.json", "/owners/red"), first); Assert.Contains(new ReferenceTarget("d.json", "/values/0"), first);
        Set("a.json", "/keyed/red", "two"); Set("a.json", "/pair", "blue/two");
        var second = map.Links(project).Where(l => l.At.Pointer == "/keyed/red").Select(l => l.Result.Target).ToArray();
        Assert.Contains(new ReferenceTarget("c.json", "/owners/red"), second); Assert.Contains(new ReferenceTarget("d.json", "/values/1"), second);
        var pair = Link("/pair"); Assert.Equal(ReferenceOutcome.Found, pair.Outcome);
        Assert.Equal(new[] { new ReferenceTarget("c.json", "/owners/blue"), new ReferenceTarget("d.json", "/values/1") }, pair.Parts);
        Assert.Contains(new ReferenceTarget("a.json", "/pair"), map.ReferencesTo(project, new("c.json", "/owners/blue")));
    }
    [Fact]
    public void Reopened_or_temporarily_unreadable_targets_cannot_keep_an_old_answer()
    {
        Assert.Equal(ReferenceOutcome.Found, Link("/pair").Outcome); var target = project.Find("d.json")!;
        target.Close(); File.WriteAllText(target.Path, "{"); Assert.Equal(ReferenceOutcome.NotLoaded, Link("/pair").Outcome);
        File.WriteAllText(target.Path, """{"values":["two"]}"""); Assert.Equal(ReferenceOutcome.Missing, Link("/pair").Outcome);
        target.Close(); File.WriteAllText(target.Path, """{"values":["one","two"]}"""); Assert.Equal(ReferenceOutcome.Found, Link("/pair").Outcome);
    }
}
