using Xunit;
using App = Brutal.Authoring.Launcher.Program;
using Cli = Brutal.Authoring.Agent.Cli.Program;

namespace Brutal.Authoring.Tests;

// These call the existing injected-writer API, not Main or native console attachment.
public sealed class BedIdentityA338Tests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    public void Help_names_BED_without_opening_a_project(string option)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var settings = Path.Combine(Path.GetTempPath(), "bed-help-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(0, App.Run([option], output, errors, settings));
        Assert.Contains("usage: BED ", output.ToString());
        Assert.Contains("BRUTAL game development framework", output.ToString());
        Assert.Contains("NOTICE.txt", output.ToString());
        Assert.Equal("", errors.ToString());
        Assert.False(Directory.Exists(settings));
    }

    [Fact]
    public void Bad_arguments_keep_the_existing_error_channel_and_exit_code()
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        Assert.Equal(2, App.Run(["--unknown-bed-option"], output, errors, "unused-settings"));
        Assert.Equal("", output.ToString());
        Assert.Contains("usage: BED ", errors.ToString());
    }

    [Fact]
    public void Command_line_usage_names_bed_cli_and_retains_its_error_exit()
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        Assert.Equal(2, Cli.Run([], new StringReader(""), output, errors));
        Assert.Equal("", output.ToString());
        Assert.Contains("usage: bed-cli ", errors.ToString());
    }
}
