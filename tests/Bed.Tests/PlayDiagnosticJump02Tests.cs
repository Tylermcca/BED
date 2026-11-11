using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Bed.Runtime;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class PlayDiagnosticJump02Tests
{
    [Theory]
    [InlineData("../outside.json", "")][InlineData("/root.json", "")][InlineData("a//b", "")]
    [InlineData("a/./b", "")][InlineData("a\\b", "")][InlineData("a:b", "")]
    [InlineData("data/a.json", "bad")][InlineData("data/a.json", "/a~2b")]
    public void Formatter_refuses_invalid_paths_and_pointers(string file,string pointer)
        =>Assert.Throws<ArgumentException>(()=>ProjectDiagnostic.Format(file,pointer,"Problem"));
    [Fact]
    public void Formatter_preserves_Unicode_escaped_keys_and_message_as_a_single_bounded_line()
    {
        var line=ProjectDiagnostic.Format("data/界.json","/a~1b~0c","A line\nwith \"quotes\" and 🚀");
        Assert.DoesNotContain('\n',line);Assert.StartsWith("BED_DIAGNOSTIC_V1 ",line);
        using var json=JsonDocument.Parse(line[18..]);Assert.Equal("/a~1b~0c",json.RootElement.GetProperty("pointer").GetString());
        Assert.Throws<ArgumentException>(()=>ProjectDiagnostic.Format("data/a.json","",new string('界',1500)));
        Assert.ThrowsAny<ArgumentException>(()=>ProjectDiagnostic.Format("data/a.json","/\ud800","Invalid Unicode"));
    }
    [Fact]
    public void Reader_refuses_invalid_Unicode_byte_overflow_extra_fields_and_line_breaks()
    {
        Assert.Null(Read("BED_DIAGNOSTIC_V1 {\"file\":\"data/a.json\",\"pointer\":\"/\\ud800\",\"message\":\"Bad\"}"));
        static object? Read(string line)=>typeof(DataWorkspace).Assembly.GetType("Bed.Editor.ImGui.PlayDiagnostic")!.GetMethod("Read",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,[line]);
        var valid=ProjectDiagnostic.Format("data/a.json","","Message");
        Assert.NotNull(Read(valid));
        Assert.Null(Read(valid+"\n"));
        Assert.Null(Read(valid+new string(' ',4096)));
        Assert.Null(Read("BED_DIAGNOSTIC_V1 {\"file\":\"data/a.json\",\"pointer\":\"\",\"message\":\""+new string('界',1400)+"\"}"));
    }
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("jump02-").FullName;
        public readonly HeadlessHost Host=new(1500,1100){Drawing=false};
        public readonly ProjectLauncher Launcher;
        public DataWorkspace Workspace=>Launcher.Workspace!;
        private readonly Dictionary<string,byte[]> original=[];
        public Rig(string? stdout=null,string? stderr=null)
        {
            Directory.CreateDirectory(Path.Combine(Root,"data"));Directory.CreateDirectory(Path.Combine(Root,"schemas"));
            foreach(var name in new[]{"source","target","other"})
            {
                var path=Path.Combine(Root,"data",name+".json");File.WriteAllText(path,"{\"items\":[{\"name\":\"First\"},{\"name\":\"Second\"}]}");original[path]=File.ReadAllBytes(path);
                File.WriteAllText(Path.Combine(Root,"schemas",name+".schema.json"),"{}");
            }
            foreach(var suffix in new[]{".exe",".dll",".deps.json",".runtimeconfig.json"})
                File.Copy(Path.Combine(AppContext.BaseDirectory,"ProjectPlayTestGame","ProjectPlayTestGame"+suffix),Path.Combine(Root,"ProjectPlayTestGame"+suffix));
            stdout??=ProjectDiagnostic.Format("data/target.json","/items/0/name","Stdout finding.");
            stderr??=ProjectDiagnostic.Format("data/other.json","/items/1/name","Stderr finding.");
            PlaySettingsStore.Save(Root,new("ProjectPlayTestGame.exe",["diagnostic",stdout,stderr],"."));
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            Launcher=Bed.Launcher.Program.SetUp(new ProjectLauncher(new RecentProjects(Path.Combine(Root,"local/recent.json")))) ;
            Assert.True(Launcher.OpenFolder(Root),Launcher.Said);Assert.True(Workspace.Show("source.json"),Workspace.Said);Workspace.Window!.Editor.Select("/items/1");
            Host.Frames(Workspace.Draw,3);Assert.True(Workspace.RequestProjectPlay(),Workspace.Said);Host.Frame(Workspace.Draw);
            ImGui.SetWindowSize("Play console###project.play.console"u8,new Brutal.Numerics.float2(1000,750));
            var timer=Stopwatch.StartNew();
            while(timer.Elapsed<TimeSpan.FromSeconds(10)&&(Workspace.ProjectPlay?.HasCompleted!=true||Workspace.ProjectPlay.ExitCode is null)){Host.Frame(Workspace.Draw);Thread.Sleep(10);}
            Assert.True(Workspace.ProjectPlay!.HasCompleted,Workspace.ProjectPlay.Status);Assert.Equal(0,Workspace.ProjectPlay.ExitCode);Host.Frames(Workspace.Draw,4);
        }
        public void Key(ImGuiKey key){Host.Press(Workspace.Draw,key);Host.Frames(Workspace.Draw,2);}
        public UiNode Link(string file)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Link&&n.Label.StartsWith("Open "+file,StringComparison.Ordinal));
        public void Console()
        {
            var io=ImGui.GetIO();io.AddKeyEvent(ImGuiKey.Mod_Ctrl,true);
            try
            {
                var found=false;
                for(var i=0;i<24;i++)
                {
                    Key(ImGuiKey.Tab);var window=ImGui.GetCurrentContext().NavWindowingTarget;
                    var title=window.IsNull()?"":Marshal.PtrToStringUTF8((IntPtr)window.Name)??"";
                    if(title.Contains("project.play.console",StringComparison.Ordinal)){found=true;break;}
                }
                Assert.True(found,"Native keyboard navigation did not reach Play console.");
            }
            finally{io.AddKeyEvent(ImGuiKey.Mod_Ctrl,false);Host.Frames(Workspace.Draw,3);}
        }
        public void Click(string file){Console();var node=Link(file);Host.Click(Workspace.Draw,node.Bounds.X+20,node.Bounds.Y+node.Bounds.Height/2);Host.Frames(Workspace.Draw,3);}
        public void Unchanged(){foreach(var (path,bytes) in original)Assert.Equal(bytes,File.ReadAllBytes(path));}
        public void Dispose(){Launcher.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Actual_game_stdout_and_stderr_links_open_the_exact_field_by_click_or_keyboard_and_Back_returns(bool keyboard)
    {
        using var r=new Rig();var file=keyboard?"data/other.json":"data/target.json";r.Console();
        if(keyboard)
        {
            for(var i=0;!r.Link(file).Focused&&i<24;i++)r.Key(ImGuiKey.Tab);
            Assert.True(r.Link(file).Focused);r.Key(ImGuiKey.Enter);
        }
        else r.Click(file);
        Assert.Equal(keyboard?"other.json":"target.json",r.Workspace.Active!.Name);
        Assert.Equal(keyboard?"/items/1":"/items/0",r.Workspace.Window!.Editor.Primary);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="name"&&n.Focused);
        Assert.True(r.Workspace.Back());Assert.Equal("source.json",r.Workspace.Active.Name);Assert.Equal("/items/1",r.Workspace.Window!.Editor.Primary);
        Assert.Empty(r.Workspace.Project.Dirty);r.Unchanged();
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Dirty_or_changed_saved_inputs_refuse_old_positions_without_navigation(bool disk)
    {
        using var r=new Rig();
        if(disk)File.AppendAllText(Path.Combine(r.Root,"data/target.json")," ");
        else Assert.True(r.Workspace.Window!.Editor.Session.Execute(new SetValue("/items/0/name",JsonSerializer.SerializeToElement("Own draft"))));
        r.Click("data/target.json");Assert.Equal("source.json",r.Workspace.Active!.Name);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label.Contains("changed since Play began",StringComparison.Ordinal));
        if(!disk){Assert.Equal(1,r.Workspace.Window!.Editor.Session.History.UndoCount);r.Unchanged();}
    }
    [Fact]
    public void A_valid_but_unadmitted_file_refuses_with_a_plain_reason()
    {
        using var r=new Rig(ProjectDiagnostic.Format("private/unknown.json","/items/0","Missing"));r.Click("private/unknown.json");
        Assert.Equal("source.json",r.Workspace.Active!.Name);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label.Contains("not available in this project",StringComparison.Ordinal));r.Unchanged();
    }
    [Theory]
    [InlineData("ordinary game output")]
    [InlineData("BED_DIAGNOSTIC_V1 {\"file\":\"data/target.json\",\"pointer\":\"\",\"message\":\"A\",\"extra\":1}")]
    [InlineData("BED_DIAGNOSTIC_V1 {\"file\":\"data/target.json\",\"file\":\"data/target.json\",\"message\":\"A\"}")]
    [InlineData("BED_DIAGNOSTIC_V1 {\"file\":\"../outside.json\",\"pointer\":\"\",\"message\":\"A\"}")]
    public void Unrecognised_output_remains_plain_text(string text)
    {
        using var r=new Rig(text,"ordinary stderr");r.Console();
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Link&&n.Label.StartsWith("Open data/",StringComparison.Ordinal));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label==text);r.Unchanged();
    }
}
