using System.Reflection;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>The isolation rule: the shared library references no game and, until decision D3, no BRUTAL package.
/// A game assembly is anything outside the framework (System, Microsoft, netstandard) and this library.</summary>
public class IsolationTests
{
    [Fact]
    public void CoreReferencesNoGameAndNoEngine()
    {
        var refs = typeof(AuthoringInfo).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToList();
        Assert.All(refs, n => Assert.True(Framework(n) || n.StartsWith("Brutal.", StringComparison.Ordinal), $"{n} isn't the framework or this library"));
        Assert.DoesNotContain(refs, n => n.StartsWith("Brutal.", StringComparison.Ordinal) && !n.StartsWith("Brutal.Authoring", StringComparison.Ordinal));
    }

    private static bool Framework(string name) =>
        name is "netstandard" or "mscorlib" || name.StartsWith("System", StringComparison.Ordinal) || name.StartsWith("Microsoft.", StringComparison.Ordinal);

    [Fact]
    public void FollowsTheCurrentPlan() => Assert.Equal("worksheet_r2", AuthoringInfo.PlanRevision);

    [Fact]
    public void TheSampleUsesOnlyTheCore()
    {
        var refs = typeof(Toy.Board).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToList();
        Assert.Contains("Brutal.Authoring", refs);
        Assert.All(refs, n => Assert.True(Framework(n) || n.StartsWith("Brutal.", StringComparison.Ordinal), $"{n} isn't the framework or this library"));
        Assert.DoesNotContain(refs, n => n.StartsWith("Brutal.", StringComparison.Ordinal) && n != "Brutal.Authoring");
    }
}
