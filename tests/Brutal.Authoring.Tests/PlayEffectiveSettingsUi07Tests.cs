using System.Text.Json;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class PlayEffectiveSettingsUi07Tests
{
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("play-effective-ui07-").FullName;
        public string Project=>Path.Combine(Root,"project");
        public string Local=>Path.Combine(Root,"local");
        public string Settings=>Path.Combine(Project,".bed","project.json");
        public readonly HeadlessHost Host=new(1400,950){Drawing=false};
        public Rig()
        {
            Directory.CreateDirectory(Path.Combine(Project,"bin"));Directory.CreateDirectory(Path.Combine(Project,"run"));
            File.WriteAllText(Path.Combine(Project,"bin","game.exe"),"inert fixture, never run");
            File.WriteAllText(Path.Combine(Project,"custom.exe"),"inert override, never run");
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
        }
        public PlaySettings Defaults=>new("bin/../bin/game.exe",["--project","."],"run",new("dotnet",["build","src/Example.csproj","-c","Release"]));
        public PlaySettingsForm Form(PlaySettings? defaults=null)
        {var form=new PlaySettingsForm(Project,defaults,Local){Open=true};Host.Frames(form.Draw,3);return form;}
        public static UiNode Text(string label)
        {
            var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label==label);
            Assert.Equal(UiActions.None,node.Actions);return node;
        }
        public static void AssertCommand(PlayLaunch launch)
        {
            Text("Effective program: "+launch.Run.Executable);
            Text("Effective arguments: "+JsonSerializer.Serialize(launch.Run.Arguments));
            Text("Effective working folder: "+launch.Run.WorkingDirectory);
            Text(launch.Build is {} build?"Effective build command: "+build.CommandLine():"Effective build command: Not configured.");
            if(launch.Build is {} command)Text("Effective build working folder: "+command.WorkingDirectory);
        }
        public void Dispose(){Host.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Root,StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,true);}
    }
    [Fact]
    public void Adapter_defaults_show_exact_full_run_and_PATH_build_commands_as_read_only_tree_text()
    {
        using var rig=new Rig();var form=rig.Form(rig.Defaults);Assert.True(form.TryLaunch(out var launch,out var why),why);
        Rig.AssertCommand(launch!);Assert.True(Path.IsPathFullyQualified(launch!.Run.Executable));Assert.True(Path.IsPathFullyQualified(launch.Build!.Executable));
        Assert.Equal(Path.Combine(rig.Project,"bin","game.exe"),launch.Run.Executable);Assert.Equal(Path.Combine(rig.Project,"run"),launch.Run.WorkingDirectory);
        Assert.Equal(rig.Project,launch.Build.WorkingDirectory);Assert.EndsWith("dotnet.exe",launch.Build.Executable,StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(rig.Settings));Assert.False(Directory.Exists(rig.Local));
        Assert.Equal("inert fixture, never run",File.ReadAllText(launch.Run.Executable));
    }
    [Fact]
    public void Partial_project_file_overrides_program_and_arguments_while_inheriting_resolved_adapter_defaults()
    {
        using var rig=new Rig();Directory.CreateDirectory(Path.GetDirectoryName(rig.Settings)!);
        const string json="""{"other":{"kept":true},"play":{"program":"custom.exe","arguments":["--seconds","0"]}}""";File.WriteAllText(rig.Settings,json);
        var form=rig.Form(rig.Defaults);Assert.True(form.TryLaunch(out var launch,out var why),why);Rig.AssertCommand(launch!);
        Assert.Equal(Path.Combine(rig.Project,"custom.exe"),launch!.Run.Executable);Assert.Equal(new[]{"--seconds","0"},launch.Run.Arguments);
        Assert.Equal(Path.Combine(rig.Project,"run"),launch.Run.WorkingDirectory);Assert.NotNull(launch.Build);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Effective program: "+Path.Combine(rig.Project,"bin","game.exe"));
        Assert.Equal(json,File.ReadAllText(rig.Settings));Assert.False(Directory.Exists(rig.Local));
    }
    [Fact]
    public void Unsaved_edits_leave_effective_saved_command_visible_until_explicit_save()
    {
        using var rig=new Rig();PlaySettingsStore.Save(rig.Project,new("bin/game.exe",["--old"]));var original=File.ReadAllBytes(rig.Settings);
        var form=rig.Form();Assert.True(form.TryLaunch(out var before,out _));Rig.AssertCommand(before!);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Program"&&n.Focused);
        rig.Host.Press(form.Draw,ImGuiKey.A,ctrl:true);rig.Host.Type(form.Draw,"custom.exe");rig.Host.Press(form.Draw,ImGuiKey.Tab);
        rig.Host.Press(form.Draw,ImGuiKey.A,ctrl:true);rig.Host.Type(form.Draw,"""["--changed"]""");rig.Host.Press(form.Draw,ImGuiKey.Tab);
        Rig.AssertCommand(before!);Assert.Equal(original,File.ReadAllBytes(rig.Settings));
        Rig.Text("These are the settings Play will use. Save Play settings to use your edits.");
        for(var i=0;i<15&&!Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.Button&&n.Label=="Save Play settings"&&n.Focused);i++)rig.Host.Press(form.Draw,ImGuiKey.Tab);
        Assert.True(Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Save Play settings").Focused);
        rig.Host.Press(form.Draw,ImGuiKey.Enter);rig.Host.Frames(form.Draw,2);Assert.Equal("Play settings saved.",form.Status);
        Assert.True(form.TryLaunch(out var after,out _));Rig.AssertCommand(after!);
        Assert.Equal(Path.Combine(rig.Project,"custom.exe"),after!.Run.Executable);Assert.Equal(new[]{"--changed"},after.Run.Arguments);Assert.Null(after.Build);
        Assert.False(Directory.Exists(rig.Local));Assert.Equal("inert override, never run",File.ReadAllText(after.Run.Executable));
    }
    [Fact]
    public void Invalid_saved_settings_report_plain_failure_without_showing_a_stale_effective_command()
    {
        using var rig=new Rig();PlaySettingsStore.Save(rig.Project,new("bin/game.exe"));var form=rig.Form();Assert.True(form.TryLaunch(out var before,out _));
        const string invalid="""{"play":{"program":"custom.exe","arguments":"wrong type"}}""";File.WriteAllText(rig.Settings,invalid);
        rig.Host.Frames(form.Draw,2);Assert.False(form.TryLaunch(out _,out var why));Rig.Text("Effective command unavailable: "+why);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.StartsWith("Effective program:",StringComparison.Ordinal));
        Assert.Equal(invalid,File.ReadAllText(rig.Settings));Assert.False(Directory.Exists(rig.Local));
    }
}
