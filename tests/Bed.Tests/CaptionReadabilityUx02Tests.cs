using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class CaptionReadabilityUx02Tests
{
    [Theory][InlineData(90,true)][InlineData(500,false)]
    public void Native_captions_and_preview_are_measured_in_the_parent_clip(int width,bool clipped)
    {
        using var host=new HeadlessHost(800,600){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());
        var input=new ImInputString(128,"content"u8);double amount=1;var check=false;
        void Draw()
        {
            ImGui.SetNextWindowPos(new(0,0));ImGui.SetNextWindowSize(new(width,500));
            if(Ui.Begin("Captions"u8))
            {
                ImGui.SetNextItemWidth(60);Ui.InputText("Long input caption"u8,input);
                ImGui.SetNextItemWidth(60);Ui.InputDouble("Long numeric caption"u8,ref amount);
                Ui.Checkbox("Long checkbox caption"u8,ref check);
                Ui.Selectable("Long selectable caption"u8);
                if(Ui.TreeNodeEx("Long tree caption"u8))Ui.TreePop();
                ImGui.SetNextItemWidth(width-30);if(Ui.BeginCombo("##choice"u8,"Long preview content"u8)){Ui.Selectable("Choice"u8);Ui.EndCombo();}
            }
            Ui.End();
        }
        host.Frames(Draw,4);
        foreach(var label in new[]{"Long input caption","Long numeric caption","Long checkbox caption","Long selectable caption","Long tree caption",""})
        {
            var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==label&&n.Role!=UiRole.Window);
            Assert.NotNull(node.TextEvidence);Assert.Equal(clipped,node.TextEvidence.Value.Truncated);
        }
        Assert.Equal("content",Assert.Single(Ui.Snapshot().Nodes,n=>n.Label=="Long input caption").Value);
    }
    [Fact]
    public void Keyboard_navigation_reveals_complete_controls_at_a_scroll_page_edge()
    {
        using var host=new HeadlessHost(800,600){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());var initial=true;
        void Draw()
        {
            ImGui.SetNextWindowPos(new(0,0));ImGui.SetNextWindowSize(new(250,180));
            if(Ui.Begin("Rows"u8))for(var i=0;i<20;i++){if(initial&&i==0)ImGui.SetKeyboardFocusHere();Ui.SmallButton("Row "+i);}
            Ui.End();initial=false;
        }
        host.Frames(Draw,4);
        for(var i=0;i<19;i++){host.Press(Draw,ImGuiKey.Tab);host.Frames(Draw,3);}
        var last=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button&&n.Label=="Row 19");
        Assert.True(last.Focused);Assert.False(last.TextEvidence!.Value.Truncated);Assert.False(last.Bounds.IsEmpty);
        var e=last.TextEvidence.Value;Assert.True(e.FullBounds.Y>=e.Clip.Y&&e.FullBounds.Y+e.FullBounds.Height<=e.Clip.Y+e.Clip.Height);
    }
}
