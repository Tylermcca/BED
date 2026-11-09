using System.Text.RegularExpressions;
using Brutal.Authoring.Agent;
using Xunit;

namespace Brutal.Authoring.Tests;

// Source-checkout documentation contract. No server, client, session or adapter is started.
public sealed class BedSkillCommandTests
{
    [Fact]
    public void Every_documented_skill_command_is_in_the_generic_tool_list()
    {
        var text = File.ReadAllText(SkillPath());
        var rows = Regex.Matches(text, @"(?m)^\| `([a-z][a-z0-9_]*)` \|")
            .Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(22, rows.Length); // Deliberate, reviewed inventory; do not pass on an empty/truncated document.
        Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
        var documented = rows.ToHashSet(StringComparer.Ordinal);
        var offered = McpServer.Tools().Select(t => t!["name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        foreach (var command in rows) Assert.Contains(command, offered);
        // Every inline command mention must also be in the checked inventory.
        foreach (Match mention in Regex.Matches(text, @"`([a-z][a-z0-9_]*)`"))
            Assert.Contains(mention.Groups[1].Value, documented);
    }

    private static string SkillPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "Brutal.Authoring.slnx"))) continue;
            var path = Path.Combine(directory.FullName, "skill", "bed", "SKILL.md");
            Assert.True(File.Exists(path), "Apply skill/bed/SKILL.md with this test in the source checkout.");
            return path;
        }
        throw new InvalidOperationException("Run this source-checkout contract test beneath Brutal.Authoring.slnx.");
    }
}
