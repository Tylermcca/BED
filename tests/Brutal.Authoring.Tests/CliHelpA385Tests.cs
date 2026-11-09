using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class CliHelpA385Tests
{
    [Fact]
    public void Run_limit_is_part_of_check_help_not_a_separate_call()
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var code = Brutal.Authoring.Agent.Cli.Program.Run([], new StringReader(""), output, errors);
        Assert.Equal(2, code);
        var help = errors.ToString();
        var start = help.IndexOf("check <id>", StringComparison.Ordinal);
        var end = help.IndexOf("import <file>", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        Assert.Contains("(--run works only inside mcp)", help[start..end]);
        Assert.DoesNotContain("--run is refused by one-shot CLI:", help);
        Assert.Contains("mcp", help[end..]);
        Assert.Equal("", output.ToString());
    }
}
