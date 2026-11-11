using System.Text.Json;
using Xunit;

namespace Bed.Tests;

/// <summary>The plug-in licence warnings against Codex's A021 table: 54 pairs worked out from primary sources (FSF, ASF,
/// MPL FAQ, SPDX), independently of Licences.cs. A pair marked unclear (null) isn't asserted either way.</summary>
public sealed class LicencePairsTests
{
    private static readonly JsonElement Pairs = Load();

    private static JsonElement Load()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "fixtures", "licences", "pairs.json");
            if (File.Exists(f)) return JsonDocument.Parse(File.ReadAllText(f)).RootElement.GetProperty("pairs").Clone();
        }
        throw new InvalidOperationException("can't find fixtures/licences/pairs.json");
    }

    public static TheoryData<string, string> All()
    {
        var all = new TheoryData<string, string>();
        foreach (var p in Pairs.EnumerateArray()) all.Add(p.GetProperty("a").GetString()!, p.GetProperty("b").GetString()!);
        return all;
    }

    [Theory]
    [MemberData(nameof(All))]
    public void TheCheckerAgreesWithTheSourcedTable(string a, string b)
    {
        var pair = Pairs.EnumerateArray().First(p => p.GetProperty("a").GetString() == a && p.GetProperty("b").GetString() == b);
        if (pair.GetProperty("clash").ValueKind == JsonValueKind.Null) return;   // unclear in the sources: no assertion
        var expected = pair.GetProperty("clash").GetBoolean();
        var got = Licences.Clash(Licences.Choices(a), Licences.Choices(b));
        Assert.True(expected == (got is not null),
            $"{a} + {b}: the table says clash={expected} ({pair.GetProperty("why").GetString()}); the checker says {got ?? "fine"}");
    }
}
