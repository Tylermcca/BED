using System.Text.Json;
using Bed.Editor;
using Xunit;
namespace Bed.Tests;

public sealed class PlaySettingsTests
{
    [Fact]
    public void Project_fields_override_individually_and_empty_argv_clears_defaults()
    {
        var defaults=new PlaySettings("bin/game.exe",["old"],"run",new("dotnet",["build","game.csproj"],["src/**/*.cs"]));
        var settings=PlaySettings.ReadProject("""{"play":{"arguments":[],"build":{"inputs":["code/**/*.cs"]}}}""",defaults)!;
        Assert.Equal("bin/game.exe",settings.Program);Assert.Empty(settings.Arguments);Assert.Equal("run",settings.WorkingFolder);
        Assert.Equal("dotnet",settings.Build!.Command);Assert.Equal(new[]{"build","game.csproj"},settings.Build.Arguments);
        Assert.Equal(new[]{"code/**/*.cs"},settings.Build.Inputs);Assert.Equal(new[]{"old"},defaults.Arguments);
    }
    [Fact]
    public void Adapter_free_project_and_unconfigured_project_are_distinct()
    {
        Assert.Null(PlaySettings.ReadProject("{}"));
        var settings=PlaySettings.ReadProject("""{"play":{"program":"game.exe","arguments":["--background"]}}""")!;
        Assert.Equal("game.exe",settings.Program);Assert.Equal(".",settings.WorkingFolder);Assert.Null(settings.Build);
        Assert.Equal("",PlaySettings.ReadProject("""{"play":{}}""")!.Program);
    }
    [Fact]
    public void Lists_are_owned_and_serialization_preserves_unrelated_fields()
    {
        var args=new[]{"literal ; & $(value)","folder with spaces"};var inputs=new[]{"src/**/*.cs"};
        var settings=new PlaySettings("game.exe",args,".",new("tool",args,inputs));args[0]="changed";inputs[0]="changed";
        var output=PlaySettings.WriteProject("""{"theme":"dark","play":{"future":42,"build":{"custom":true}}}""",settings);
        using var json=JsonDocument.Parse(output);Assert.Equal("dark",json.RootElement.GetProperty("theme").GetString());
        Assert.Equal(42,json.RootElement.GetProperty("play").GetProperty("future").GetInt32());
        Assert.True(json.RootElement.GetProperty("play").GetProperty("build").GetProperty("custom").GetBoolean());
        var roundtrip=PlaySettings.ReadProject(output)!;Assert.Equal("literal ; & $(value)",roundtrip.Arguments[0]);
        Assert.Equal(settings.Arguments,roundtrip.Build!.Arguments);Assert.Equal(new[]{"src/**/*.cs"},roundtrip.Build.Inputs);
    }
    [Theory]
    [InlineData("[]")][InlineData("{\"play\":null}")][InlineData("{\"play\":{\"program\":3}}")]
    [InlineData("{\"play\":{\"arguments\":\"one two\"}}")][InlineData("{\"play\":{\"arguments\":[null]}}")]
    [InlineData("{\"play\":{},\"play\":{}}")][InlineData("{\"play\":{\"program\":\"a\",\"program\":\"b\"}}")]
    public void Malformed_or_ambiguous_settings_refuse_without_fallback(string json)
        =>Assert.Throws<JsonException>(()=>PlaySettings.ReadProject(json,new("default.exe")));
    [Theory]
    [InlineData("../outside.exe")][InlineData("sub/../../outside.exe")][InlineData("C:/outside.exe")]
    [InlineData("/outside.exe")][InlineData("file:stream")][InlineData("")]
    public void Settings_text_cannot_grant_external_path_authority(string value)
    {
        var root=Path.Combine(Path.GetTempPath(),"project");
        Assert.False(PlaySettings.TryProjectPath(root,value,out var full,out var why));Assert.Null(full);Assert.NotEmpty(why!);
    }
    [Fact]
    public void Lexical_resolution_neither_requires_a_file_nor_interprets_literal_arguments()
    {
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Assert.False(Directory.Exists(root));
        Assert.True(PlaySettings.TryProjectPath(root,"bin/../game.exe",out var path,out var why),why);
        Assert.Equal(Path.Combine(root,"game.exe"),path);Assert.False(File.Exists(path));Assert.False(Directory.Exists(root));
    }
    [Fact]
    public void Declarations_are_bounded_without_shell_or_process_work()
    {
        Assert.Throws<ArgumentException>(()=>new PlaySettings("a\0.exe"));
        Assert.Throws<ArgumentException>(()=>new PlaySettings("game.exe",Enumerable.Repeat("x",129)));
        Assert.Throws<ArgumentException>(()=>new PlayBuildSettings("tool",[new string('x',8193)]));
    }
}
