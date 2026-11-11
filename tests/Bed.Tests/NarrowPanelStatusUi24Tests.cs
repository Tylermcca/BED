using System.Globalization;
using System.Numerics;
using Bed;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Im=Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class NarrowPanelStatusUi24Tests
{
    private const string Notice="Open the game project to edit this read-only reference view; source ownership and validation still apply.";
    [Theory][InlineData(1f)][InlineData(1.5f)][InlineData(2f)]
    public void Undocked_muted_and_status_notices_fit_visually_and_keep_complete_names_tooltips_and_stable_ids(float scale)
    {
        using var host=new HeadlessHost(1900,800){Drawing=false};Im.GetStyle().FontScaleMain=scale;
        var width=180f;string log="";
        void Draw()
        {
            Im.SetNextWindowPos(new(10,10));Im.SetNextWindowSize(new(width,450));Im.LogToClipboard();
            Ui.Begin("Undocked notices"u8);Ui.TextDisabled(Notice,semanticId:"notice");Ui.Status(Notice,semanticId:"status");Ui.End();
            Im.LogFinish();log=Im.GetClipboardText().ToString()??"";
        }
        host.Frames(Draw,4);
        var first=Ui.Snapshot().Nodes.Where(n=>n.Label==Notice).ToArray();Assert.Equal(2,first.Length);
        Assert.Contains("...",log);Assert.DoesNotContain(Notice,log);
        foreach(var node in first)
        {
            Assert.Equal(Notice,node.Tooltip);Assert.True(node.TextEvidence!.Value.Truncated);
            Assert.True(node.Bounds.Width<=node.TextEvidence.Value.Clip.Width+1);
            Assert.True(node.Bounds.X+node.Bounds.Width<=node.TextEvidence.Value.Clip.X+node.TextEvidence.Value.Clip.Width+1);
            Assert.True(node.Bounds.Height>0);Assert.Equal(UiActions.None,node.Actions);
        }
        var ids=first.Select(n=>n.Id).ToArray();host.Frames(Draw,2);Assert.Equal(ids,Ui.Snapshot().Nodes.Where(n=>n.Label==Notice).Select(n=>n.Id));
        width=1880;host.Frames(Draw,3);Assert.Contains(Notice,log);
        foreach(var node in Ui.Snapshot().Nodes.Where(n=>n.Label==Notice)){Assert.False(node.TextEvidence!.Value.Truncated);Assert.Null(node.Tooltip);}
    }
    [Fact]
    public void Explicit_wrap_stays_wrapped_and_Unicode_ellipsis_keeps_complete_graphemes()
    {
        using var host=new HeadlessHost(700,650){Drawing=false};string log="";
        var unicode="Unicode: "+string.Concat(Enumerable.Repeat("e\u0301\U0001f680",30));
        void Draw()
        {
            Im.SetNextWindowPos(new(10,10));Im.SetNextWindowSize(new(210,500));Im.LogToClipboard();Ui.Begin("Wrapped notice"u8);
            Ui.TextDisabled(unicode,semanticId:"unicode");Im.PushTextWrapPos(0);Ui.Status(Notice,semanticId:"wrapped");Im.PopTextWrapPos();Ui.End();Im.LogFinish();log=Im.GetClipboardText().ToString()??"";
        }
        host.Frames(Draw,4);var wrapped=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==Notice);
        Assert.True(wrapped.TextEvidence!.Value.WrapWidth>0);Assert.False(wrapped.TextEvidence.Value.Truncated);Assert.True(wrapped.Bounds.Height>Im.GetTextLineHeight()*2);
        var painted=Assert.Single(log.Split('\n').Select(x=>x.Trim()),x=>x.StartsWith("Unicode:",StringComparison.Ordinal));
        Assert.EndsWith("...",painted);var prefix=painted[..^3];
        Assert.StartsWith(prefix,unicode);Assert.Contains(prefix.Length,StringInfo.ParseCombiningCharacters(unicode));
        Assert.False(char.IsHighSurrogate(prefix[^1]));Assert.Equal(unicode,Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==unicode).Tooltip);
    }
    [Theory][InlineData(1f)][InlineData(2f)]
    public void Narrow_viewport_overlay_notices_and_full_selection_are_accessible_without_clipped_status_bounds(float scale)
    {
        const string id="generated_preview_with_a_long_but_valid_name_for_a_narrow_panel";
        const string reason="Generated geometry is read-only. Edit its source and rebuild before moving, rotating or scaling.";
        var scene=new Scene([SceneObject.At(id,null,new(1,2,3))]);var editor=new EditorModel<Scene>(new(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),scene),SceneEditing.Outline);editor.Select(id);
        using var host=new HeadlessHost(700,850){Drawing=false};Im.GetStyle().FontScaleMain=scale;EditorWindow<Scene>.Configure(Im.GetIO());
        var view=new ViewportView<Scene>(editor,new SceneViewport());
        var guard=Assert.Single(typeof(ViewportView<Scene>).GetMember("TransformRefusal",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic));
        if(guard is System.Reflection.PropertyInfo property)property.SetValue(view,(Func<string?>)(()=>reason));
        else Assert.IsAssignableFrom<System.Reflection.FieldInfo>(guard).SetValue(view,(Func<string?>)(()=>reason));
        void Draw(){Im.SetNextWindowPos(new(10,10));Im.SetNextWindowSize(new(330,750));Ui.Begin("Narrow preview"u8);view.Draw();Ui.End();}
        host.Frames(Draw,4);var tree=Ui.Snapshot();var canvas=Assert.Single(tree.Nodes,n=>n.Role==UiRole.Canvas);
        var statuses=tree.Nodes.Where(n=>n.Parent>=0&&tree.Nodes[n.Parent].Role==UiRole.Canvas&&n.Role==UiRole.Status).ToArray();Assert.Equal(3,statuses.Length);
        foreach(var node in statuses)
        {
            Assert.NotNull(node.TextEvidence);Assert.False(node.TextEvidence.Value.Truncated);Assert.Equal(node.Label,node.Tooltip);
            Assert.True(node.TextEvidence.Value.FullBounds.Y+node.TextEvidence.Value.FullBounds.Height<=canvas.Bounds.Y+canvas.Bounds.Height+1);
            Assert.True(node.Bounds.X+node.Bounds.Width<=canvas.Bounds.X+canvas.Bounds.Width+1);
            Assert.Equal(canvas.ViewportId,node.ViewportId);
        }
        Assert.Contains(statuses,n=>n.Label==reason);Assert.Contains(statuses,n=>n.Label.Contains(id,StringComparison.Ordinal)&&n.Label.Contains("X 1, Y 2, Z 3",StringComparison.Ordinal));
        var identities=statuses.Select(n=>n.Id).ToArray();host.Frames(Draw,3);Assert.Equal(identities,Ui.Snapshot().Nodes.Where(n=>n.Parent>=0&&Ui.Snapshot().Nodes[n.Parent].Role==UiRole.Canvas&&n.Role==UiRole.Status).Select(n=>n.Id));
        Assert.Same(scene,editor.Session.Current);Assert.False(editor.Session.Dirty);Assert.Empty(editor.Session.Journal);
        if(Environment.GetEnvironmentVariable("UI24_SHOTS") is {} folder)
        {Directory.CreateDirectory(folder);host.Drawing=true;host.Frame(Draw);host.Save(Path.Combine(folder,$"narrow-preview-{scale}.png"));}
    }
    [Fact]
    public void A_status_in_a_clipped_span_fits_that_span_instead_of_the_whole_parent_window()
    {
        using var host=new HeadlessHost(900,500){Drawing=false};string log="";
        void Draw()
        {
            Im.SetNextWindowPos(new(10,10));Im.SetNextWindowSize(new(800,400));Im.LogToClipboard();Ui.Begin("Status spans"u8);
            var at=Im.GetCursorScreenPos();var draw=Im.GetWindowDrawList();draw.PushClipRect(at,at+new Brutal.Numerics.float2(140,Im.GetTextLineHeight()),true);
            Ui.Status(Notice,semanticId:"clipped");draw.PopClipRect();Ui.End();Im.LogFinish();log=Im.GetClipboardText().ToString()??"";
        }
        host.Frames(Draw,4);var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==Notice);
        Assert.True(node.Bounds.Width<=140);Assert.Equal(Notice,node.Tooltip);Assert.True(node.TextEvidence!.Value.Truncated);
        Assert.Contains("...",log);Assert.DoesNotContain(Notice,log);
    }

}
