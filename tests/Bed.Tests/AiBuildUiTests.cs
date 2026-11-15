using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;
using Xunit;
using ImGui=Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class AiBuildUiTests
{
    [Fact]
    public void Build_click_previews_unapproved_command_without_writing_approval_and_dirty_files_are_refused()
    {
        var root=Directory.CreateTempSubdirectory("bed-build-ui-");
        try
        {
            var project=root.CreateSubdirectory("project");var tools=project.CreateSubdirectory("tools");
            File.WriteAllText(Path.Combine(tools.FullName,"builder.exe"),"fake; must never run");
            var prefs=Path.Combine(root.FullName,"preferences");
            var defaults=new PlaySettings("game.exe",build:new("tools/builder.exe",["literal-argument"]));
            var form=new PlaySettingsForm(project.FullName,defaults,prefs);var dirty=false;
            using var panel=new ProjectBuildPanel(form,new(prefs),()=>dirty,()=>false);
            using var host=new HeadlessHost(1200,900){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            void Draw()
            {
                ImGui.SetNextWindowPos(new float2(10,10),ImGuiCond.Always);ImGui.SetNextWindowSize(new float2(760,560),ImGuiCond.Always);
                if(ImGui.Begin("Build proof",ImGuiWindowFlags.NoSavedSettings))panel.Draw();ImGui.End();
            }
            void Press(string label)
            {
                host.Frames(Draw,2);var window=ImGui.Internal.FindWindowByName("Build proof");var wanted=window.GetID(label).Value;
                for(var i=0;i<30&&ImGui.GetCurrentContext().NavId.Value!=wanted;i++)host.Press(Draw,ImGuiKey.Tab);
                Assert.True(wanted==ImGui.GetCurrentContext().NavId.Value,"Tab could not reach "+label);host.Press(Draw,ImGuiKey.Enter);
            }
            string Text(){host.Frame(()=>{ImGui.LogToClipboard();Draw();ImGui.LogFinish();});return ImGui.GetClipboardText().ToString()??"";}
            host.Frames(Draw,3);Assert.False(Directory.Exists(prefs));
            Press("Build saved files");var preview=Text();Assert.Contains("literal-argument",preview);Assert.Contains("Allow this exact command",preview);
            Assert.False(Directory.Exists(prefs));Assert.False(panel.Busy);
            Press("Cancel");dirty=true;Press("Build saved files");Assert.Contains("Save changes",Text());Assert.False(panel.Busy);Assert.False(Directory.Exists(prefs));
        }
        finally{root.Delete(true);}
    }
}
public sealed class AiBuildCliTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Data_only_and_read_only_projects_refuse_without_writes(bool readOnly)
    {
        var root=Directory.CreateTempSubdirectory("bed-build-cli-");
        try
        {
            var data=root.CreateSubdirectory("data");var schemas=root.CreateSubdirectory("schemas");
            var args=new List<string>{"--data",data.FullName,"--schemas",schemas.FullName};if(readOnly)args.Add("--read-only");args.Add("build");
            using var stdout=new StringWriter();using var stderr=new StringWriter();
            Assert.Equal(1,Bed.Agent.Cli.Program.Run(args.ToArray(),new StringReader(""),stdout,stderr));
            Assert.Contains(readOnly?"read-only":"no game build",stdout.ToString());Assert.Empty(stderr.ToString());
            Assert.Empty(data.GetFiles());Assert.Empty(schemas.GetFiles());Assert.False(Directory.Exists(Path.Combine(root.FullName,".bed")));
        }
        finally{root.Delete(true);}
    }
}
