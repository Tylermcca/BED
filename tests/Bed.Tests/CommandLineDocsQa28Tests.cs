using System.Text.RegularExpressions;
using Xunit;
namespace Bed.Tests;

public sealed class CommandLineDocsQa28Tests
{
    [Fact]
    public void Printed_cli_usage_and_guide_list_exactly_the_same_commands_and_flags()
    {
        using var output=new StringWriter();using var errors=new StringWriter();
        Assert.Equal(0,Bed.Agent.Cli.Program.Run(["--help"],TextReader.Null,output,errors));
        Assert.Equal("",errors.ToString());var help=output.ToString().Replace("\r\n","\n",StringComparison.Ordinal);
        var repo=new DirectoryInfo(AppContext.BaseDirectory);
        while(!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent!;
        var guide=File.ReadAllText(Path.Combine(repo.FullName,"docs","guide","command_line.md")).Replace("\r\n","\n",StringComparison.Ordinal);
        var commands=Rows(Section(guide,"bed-cli commands"));
        var documentedFlags=Rows(Section(guide,"bed-cli options"));
        Assert.Equal(commands.Length,commands.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(documentedFlags.Length,documentedFlags.Distinct(StringComparer.Ordinal).Count());
        var signatures=help.Split('\n').Where(line=>Regex.IsMatch(line,"^  [a-z]"))
            .Select(line=>Regex.Split(line[2..]," {2,}",RegexOptions.None)[0].Trim().Replace("allowed|forbidden","allowed or forbidden",StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(signatures);
        Assert.Equal(commands.Order(StringComparer.Ordinal),signatures.Order(StringComparer.Ordinal));
        var printedFlags=Regex.Matches(help,"--[a-z][a-z-]*").Select(m=>m.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        Assert.Equal(documentedFlags.Order(StringComparer.Ordinal),printedFlags);
    }
    private static string[] Rows(string text)=>Regex.Matches(text,"^\\| `([^`]+)` \\|",RegexOptions.Multiline).Select(m=>m.Groups[1].Value).ToArray();
    private static string Section(string text,string name)
    {
        var start=text.IndexOf("### "+name+"\n",StringComparison.Ordinal);Assert.True(start>=0,name);
        start+=name.Length+5;
        var headings=new[]{text.IndexOf("\n### ",start,StringComparison.Ordinal),text.IndexOf("\n## ",start,StringComparison.Ordinal)}.Where(i=>i>=0).ToArray();
        return headings.Length==0?text[start..]:text[start..headings.Min()];
    }
}
