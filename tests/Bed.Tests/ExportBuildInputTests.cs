using Bed.Editor;
using Xunit;
namespace Bed.Tests;
public sealed class ExportBuildInputTests
{
    [Fact] public void Optional_saved_input_binding_copies_whole_literal_arguments_and_requires_absolute_input()
    {
        string[] arguments=["--input","{input}","--output","{output}","literal & value"];
        var spec=new GameBuildSpec(@"C:\builder\build.exe",@"C:\builder",arguments);arguments[1]="changed";
        Assert.True(spec.AcceptsSavedInput);
        Assert.Throws<ArgumentException>(()=>spec.BindOutput(@"C:\owned\output"));
        Assert.Throws<ArgumentException>(()=>spec.BindOutput(@"C:\owned\output","relative-input"));
        var command=spec.BindOutput(@"C:\owned\output",@"C:\owned\captured input");
        Assert.Equal(new[]{"--input",@"C:\owned\captured input","--output",@"C:\owned\output","literal & value"},command.Arguments);
        Assert.Equal("{input}",spec.Arguments[1]);Assert.Equal("{output}",spec.Arguments[3]);
    }
    [Theory][InlineData("duplicate")][InlineData("embedded")]
    public void Input_placeholder_is_optional_but_never_duplicate_or_embedded(string mode)
    {
        string[] arguments=mode=="duplicate"?["{input}","{input}","{output}"]:["--input={input}","{output}"];
        Assert.Throws<ArgumentException>(()=>new GameBuildSpec(@"C:\builder\build.exe",@"C:\builder",arguments));
    }
    [Fact] public void Legacy_builder_does_not_gain_a_saved_input_claim()
    {
        var legacy=new GameBuildSpec(@"C:\builder\build.exe",@"C:\builder",["{output}"]);
        Assert.False(legacy.AcceptsSavedInput);Assert.Equal(new[]{@"C:\owned\output"},legacy.BindOutput(@"C:\owned\output").Arguments);
    }
}
