using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class ReadabilityUi37Tests
{
    private static string Repo(){var p=new DirectoryInfo(AppContext.BaseDirectory);while(p is not null&&!File.Exists(Path.Combine(p.FullName,"Bed.slnx")))p=p.Parent;return p!.FullName;}
    private static void Capture(HeadlessHost host,Action draw,string name){host.Drawing=true;host.Frame(draw);host.Drawing=false;var folder=Path.Combine(Repo(),".validation/UI37");Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,name+".png"),Png.Encode(host.Width,host.Height,host.Pixels));}
    private sealed class Rig:IDisposable
    {
        internal readonly string Root=Directory.CreateTempSubdirectory("readable-ui-").FullName;internal readonly HeadlessHost Host;internal readonly ProjectLauncher Launcher;
        internal Rig(int width=1280,int height=800){Host=new(width,height){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());Launcher=new(new RecentProjects(Path.Combine(Root,"preferences/recent.json")));}
        internal void Frames()=>Host.Frames(Launcher.Draw,4);
        public void Dispose(){Launcher.Dispose();Host.Dispose();Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);Directory.Delete(Root,true);}
    }
    [Theory][InlineData(1280,800,1f)][InlineData(720,1000,2f)]
    public void Start_screen_fills_the_work_area_and_keeps_named_primary_actions_visible(int width,int height,float zoom)
    {
        using var r=new Rig(width,height);EditorWindow<DataDocument>.SetTextScale(zoom);r.Frames();var nodes=Ui.Snapshot().Nodes;var root=Assert.Single(nodes,n=>n.Role==UiRole.Window&&n.Label=="BED");Assert.InRange(root.Bounds.Width,width-1,width+1);Assert.InRange(root.Bounds.Height,height-1,height+1);Assert.Equal(16,ImGui.GetStyle().FontSizeBase);Assert.Equal(zoom,EditorWindow<DataDocument>.TextScale);
        foreach(var name in new[]{"New project...","Open project...","Try the example"}){var button=Assert.Single(nodes,n=>n.Role==UiRole.Button&&n.Label==name);Assert.True(button.Enabled,button.DisabledReason);Assert.False(button.Bounds.IsEmpty);Assert.InRange(button.Bounds.Y,0,height-button.Bounds.Height);}
        Assert.Contains(nodes,n=>n.Label.StartsWith("Edit game data and levels.",StringComparison.Ordinal));Assert.Contains(nodes,n=>n.Role==UiRole.Window&&n.Label=="Recent projects"&&n.Bounds.Height>=120);Capture(r.Host,r.Launcher.Draw,zoom==1?"after-start":"after-start-200-percent");
    }
    [Fact]
    public void Windows_text_scale_composes_with_monitor_DPI_and_preserves_the_saved_zoom()
    {
        using var host=new HeadlessHost(){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());Assert.True(EditorWindow<Scene>.SetTextScale(1.25f));var text=1.5f;var dpi=new DpiScale{TextScaleReader=()=>text};Assert.True(dpi.Apply(2));float size=0;host.Frames(()=>{Ui.Begin("Text scale proof"u8);Ui.Text("Readable text"u8);size=ImGui.GetFontSize();Ui.End();},4);Assert.InRange(size,59,61);Assert.Equal(1.25f,EditorWindow<Scene>.TextScale);Assert.Equal(3,ImGui.GetStyle().FontScaleDpi);var padding=ImGui.GetStyle().FramePadding;text=1.75f;Assert.True(dpi.Apply(2));Assert.Equal(padding,ImGui.GetStyle().FramePadding);Assert.Equal(3.5f,ImGui.GetStyle().FontScaleDpi);Assert.False(dpi.Apply(2));
    }
    [Fact]
    public unsafe void Sample_table_uses_content_widths_without_horizontal_scroll_at_1280()
    {
        using var r=new Rig();var data=Directory.CreateDirectory(Path.Combine(r.Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(r.Root,"schemas")).FullName;foreach(var file in Directory.GetFiles(Path.Combine(Repo(),"samples/StarterProject/data"),"*.json"))File.Copy(file,Path.Combine(data,Path.GetFileName(file)));foreach(var file in Directory.GetFiles(Path.Combine(Repo(),"samples/StarterProject/schemas"),"*.json"))File.Copy(file,Path.Combine(schemas,Path.GetFileName(file)));Assert.True(r.Launcher.Open(new(data,schemas)),r.Launcher.Said);var workspace=r.Launcher.Workspace!;Assert.True(workspace.Show("wares.json"));var table=workspace.TableOf("wares.json")!;table.List="/wares";workspace.Window!.Editor.SetOpen("editor.table",true);r.Frames();var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Table);var native=ImGui.Internal.TableFindByID(new ImGuiID((uint)node.Id));Assert.True(native.Ptr!=null);Assert.InRange(native.InnerWindow.ScrollMax.X,0,.1f);Assert.Equal(table.Columns().Count+1,native.ColumnsCount);Assert.Equal(5,table.Visible().Count);var nameCell=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label=="Clay pot: name");Assert.True(nameCell.Bounds.Width>=ImGui.CalcTextSize("Clay pot"u8).X+ImGui.GetStyle().FramePadding.X*2+1);Capture(r.Host,r.Launcher.Draw,"after-sample-table");
    }
    private sealed class SceneAdapter:IProjectAdapter
    {
        public string Name=>"Readability fixture";public bool Recognises(ProjectPaths p)=>File.Exists(Path.Combine(p.Data,"level.json"));
        public DataProject Open(ProjectPaths p){var project=DataProject.Open(p.Data,p.Schemas);Assert.True(DocumentHost.For(project).BindScene("level.json","scene",out _));return project;}
        public void Configure(EditorModel<DataDocument> editor,DataTableView table){}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Scene_projects_open_their_level_immediately_with_one_typed_authority(bool flat)
    {
        using var r=new Rig();var data=Directory.CreateDirectory(Path.Combine(r.Root,"data")).FullName;var schemas=Directory.CreateDirectory(Path.Combine(r.Root,"schemas")).FullName;File.WriteAllText(Path.Combine(schemas,"tuning.schema.json"),"{}");File.WriteAllText(Path.Combine(data,"tuning.json"),"{}");File.WriteAllText(Path.Combine(data,"level.json"),new Scene2Format().Write(new(flat?SceneSpace.TwoDXY:SceneSpace.ThreeD,[],[],[],null)));r.Launcher.Loaded=[new SceneAdapter()];Assert.True(r.Launcher.Open(new(data,schemas)),r.Launcher.Said);var workspace=r.Launcher.Workspace!;Assert.Equal("level.json",workspace.Active!.Name);Assert.Null(workspace.Active.Session);Assert.True(workspace.Documents.TryGetOpenScene2("level.json",out var entry));Assert.False(entry!.Session.Dirty);r.Frames();Capture(r.Host,r.Launcher.Draw,flat?"after-open-xy-level":"after-open-3d-level");
    }
    [Fact]
    public void Rejected_field_edit_shows_the_same_reason_beside_the_named_control_then_clears_after_a_valid_edit()
    {
        using var host=new HeadlessHost(1200,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());var schemas=new JsonSchemas().Add("data","""{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"amount":{"type":"number","title":"Amount","maximum":10}}}}}}""");var session=DataKit.Session(new DataFormat().Read("{\"items\":[{\"amount\":2}]}"),schemas,"data");var editor=new EditorModel<DataDocument>(session,DataEditing.Outline);editor.Select("/items/0");var window=new EditorWindow<DataDocument>(editor,new Inspector<DataDocument>(editor).Add(DataEditing.ObjectType(schemas,"data")));host.Frames(window.DrawDocked,4);UiNode Field()=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.TextField&&n.Label.Equals("Amount",StringComparison.OrdinalIgnoreCase));var field=Field();var label=field.Label;var before=session.Current;var action=Ui.RequestAction(field.Id,UiActions.SetValue,"99");host.Frames(window.DrawDocked,3);Assert.Equal(UiActionOutcome.Applied,action.Outcome);Assert.Same(before,session.Current);Assert.Contains("10",window.Status);field=Field();var message=window.Status;Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label==message&&n.Bounds.Y>=field.Bounds.Y+field.Bounds.Height);Capture(host,window.DrawDocked,"after-inline-validation");var correct=Ui.RequestAction(field.Id,UiActions.SetValue,"4");host.Frames(window.DrawDocked,3);Assert.Equal(UiActionOutcome.Applied,correct.Outcome);Assert.Equal(4,session.Current.At("/items/0/amount")!.Value.GetInt32());Assert.Equal(label,Field().Label);Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Status&&n.Label==message);
    }
}
