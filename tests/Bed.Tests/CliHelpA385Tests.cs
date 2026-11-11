using Xunit;

namespace Bed.Tests;

public sealed class CliHelpA385Tests
{
    [Fact]
    public void Run_limit_is_part_of_check_help_not_a_separate_call()
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var code = Bed.Agent.Cli.Program.Run([], new StringReader(""), output, errors);
        Assert.Equal(2, code);
        var help = errors.ToString();
        var start = help.IndexOf("check <id>", StringComparison.Ordinal);
        var end = help.IndexOf("import <file>", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        Assert.Contains("(--run works only inside mcp)", help[start..end]);
        Assert.DoesNotContain("--run is refused by one-shot CLI:", help);
        Assert.Contains("mcp", help[end..]);
        Assert.Equal("", output.ToString());
        using var explicitOutput=new StringWriter();using var explicitErrors=new StringWriter();
        Assert.Equal(0,Bed.Agent.Cli.Program.Run(["--help"],TextReader.Null,explicitOutput,explicitErrors));
        Assert.Equal(help+Environment.NewLine,explicitOutput.ToString());Assert.Equal("",explicitErrors.ToString());
    }
    [Theory]
    [InlineData("--data", "C:/missing-project")]
    [InlineData("--version", "")]
    [InlineData("--help", "")]
    [InlineData("--json", "")]
    public void Help_with_other_arguments_refuses_before_project_admission(string flag,string value)
    {
        using var output=new StringWriter();using var errors=new StringWriter();
        string[] args=value.Length==0?["--help",flag]:["--help",flag,value];
        Assert.Equal(2,Bed.Agent.Cli.Program.Run(args,TextReader.Null,output,errors));
        Assert.Equal("",output.ToString());Assert.Equal("--help must be used alone."+Environment.NewLine,errors.ToString());
    }
    [Theory] [InlineData("-h")] [InlineData("/?")]
    public void No_unapproved_help_alias_is_admitted(string alias)
    {
        using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(2,Bed.Agent.Cli.Program.Run([alias],TextReader.Null,output,errors));
        Assert.Equal("",output.ToString());Assert.NotEqual("",errors.ToString());
    }

}
