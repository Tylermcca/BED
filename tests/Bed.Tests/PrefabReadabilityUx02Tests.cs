using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class PrefabReadabilityUx02Tests
{
    public static IEnumerable<object[]> Matrix(){foreach(var width in new[]{1280,1920})foreach(var scale in new[]{1f,1.5f,2f})foreach(var theme in new[]{EditorThemePreference.Light,EditorThemePreference.Dark})foreach(var mode in new[]{"review","slot"})yield return [width,width==1280?720:1080,scale,theme,mode];}
    [Theory][MemberData(nameof(Matrix))]
    public void Prefab_review_and_slot_choices_keep_labels_and_targets_readable(int width,int height,float scale,EditorThemePreference theme,string mode)
    {
        var previous=EditorTheme.Preference;
        try
        {
            using var r=new Scene2HostCommandTests.Rig();using var workspace=new DataWorkspace(r.Project);Assert.True(workspace.Show("level.json"));
            using var host=new HeadlessHost(width,height){Drawing=false};EditorWindow<Scene2>.Configure(ImGui.GetIO());new DpiScale().Apply(scale);EditorTheme.Preference=theme;
            var editor=workspace.Scene2Window!.Editor;editor.Select("frame_a_root");var document=editor.Session.Current;var saved=File.ReadAllText(r.Disk);host.Frames(workspace.Draw,6);
            UiNode Node(string label,UiRole role)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==role&&n.Label==label);
            void Focus(string label,UiRole role=UiRole.Button){var focus=Ui.RequestAction(Node(label,role).Id,UiActions.Focus);host.Frames(workspace.Draw,8);Assert.Equal(UiActionOutcome.Applied,focus.Outcome);}
            void Invoke(string label){Focus(label);var request=Ui.RequestAction(Node(label,UiRole.Button).Id,UiActions.Invoke);host.Frames(workspace.Draw,6);Assert.Equal(UiActionOutcome.Applied,request.Outcome);}
            if(mode=="review"){r.ChangeSource();host.Frames(workspace.Draw,4);Invoke("Review source update");Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Accept update");}
            else
            {
                editor.SetOpen("scene2.prefabs",true);host.Frames(workspace.Draw,6);Invoke("Refresh prefab sources");
                var combo=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Combo&&n.Label.StartsWith("Bind Owner",StringComparison.Ordinal));
                Focus(combo.Label,UiRole.Combo);combo=Node(combo.Label,UiRole.Combo);host.Click(workspace.Draw,combo.Bounds.X+10,combo.Bounds.Y+10);host.Frames(workspace.Draw,4);
                Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Combo&&n.Label==combo.Label&&n.Expanded==true);
            }
            var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent;
            var folder=Path.Combine(repo!.FullName,".validation","UX02");Directory.CreateDirectory(folder);var name=$"prefab-{mode}-{width}-{scale:0.0}-{theme}";
            host.Drawing=true;host.Frame(workspace.Draw);host.Save(Path.Combine(folder,name+".png"));if(mode=="review")Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.StartsWith("frame_a_root: label",StringComparison.Ordinal)&&n.TextEvidence is {Truncated:false} e&&e.FullBounds.Y>=e.Clip.Y&&e.FullBounds.Y+e.FullBounds.Height<=e.Clip.Y+e.Clip.Height);ReadabilityUx02Tests.AssertReadable(Ui.Snapshot(),scale,Path.Combine(folder,name+".txt"));
            Assert.Same(document,editor.Session.Current);Assert.Empty(editor.Session.Journal);Assert.Equal(saved,File.ReadAllText(r.Disk));
        }
        finally{EditorTheme.Preference=previous;}
    }
}
