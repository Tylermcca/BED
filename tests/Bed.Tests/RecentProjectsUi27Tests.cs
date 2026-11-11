using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
using Im=Brutal.ImGuiApi.ImGui;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class RecentProjectsUi27Tests
{
    private sealed class Spy:IProjectAdapter
    {
        public int Calls;
        public string Name=>"Self-authored spy";
        public ProjectPaths? FindProject(string folder){Calls++;return null;}
        public bool Recognises(ProjectPaths paths){Calls++;return false;}
        public DataProject Open(ProjectPaths paths){Calls++;throw new InvalidOperationException("No missing entry should reach adapter code.");}
        public void Configure(EditorModel<DataDocument> editor,DataTableView table){}
    }
    private sealed class Rig:IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("recent-ui27-").FullName;
        public readonly HeadlessHost Host=new(1400,1000){Drawing=false};
        public readonly ProjectLauncher Launcher;
        public readonly Spy Probe=new();
        public Rig(int limit=10)
        {EditorWindow<DataDocument>.Configure(Im.GetIO());Launcher=new(new RecentProjects(Path.Combine(Root,"local/recent.json"),limit)){Loaded=[Probe]};}
        public ProjectPaths Add(string name,bool present=true)
        {
            var p=new ProjectPaths(Path.Combine(Root,name,"data"),Path.Combine(Root,name,"schemas"));
            if(present){Directory.CreateDirectory(p.Data);Directory.CreateDirectory(p.Schemas);File.WriteAllText(Path.Combine(p.Data,"item.json"),"{}");File.WriteAllText(Path.Combine(p.Schemas,"item.schema.json"),"{}");}
            Launcher.Recent.Add(p);return p;
        }
        public void Frames()=>Host.Frames(Launcher.Draw,3);
        public void Key(ImGuiKey key)=>Host.Press(Launcher.Draw,key);
        public UiNode Row(string name)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label==name);
        public void Focus(string name)
        {var entered=new HashSet<uint>();for(var i=0;!Row(name).Focused&&i<60;i++){var id=Im.GetCurrentContext().NavId.Value;if(id!=0&&!Ui.Snapshot().Nodes.Any(n=>n.Focused)&&entered.Add(id))Key(ImGuiKey.Enter);else Key(ImGuiKey.Tab);}Assert.True(Row(name).Focused,Ui.Snapshot().Text);}
        public void Context(string name)
        {Focus(name);Key(ImGuiKey.Menu);Frames();}
        public void Action(string label)
        {for(var i=0;!Ui.Snapshot().Nodes.Any(n=>n.Role==UiRole.MenuItem&&n.Label==label&&n.Focused)&&i<25;i++)Key(ImGuiKey.DownArrow);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label==label&&n.Focused);Key(ImGuiKey.Enter);Frames();}
        public void Dispose(){Launcher.Dispose();Host.Dispose();Directory.Delete(Root,true);}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Missing_start_entry_stays_named_and_keyboard_removable_without_loading_adapter_or_changing_any_project(bool rootEntry)
    {
        using var r=new Rig();var keep=r.Add("keep");var absent=r.Add("absent",false);
        if(rootEntry){r.Launcher.Recent.Remove(absent);absent=absent with{Folder=Path.GetDirectoryName(absent.Data)};r.Launcher.Recent.Add(absent);}
        var history=File.ReadAllBytes(r.Launcher.Recent.File);var source=File.ReadAllBytes(Path.Combine(keep.Data,"item.json"));r.Frames();
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Folder not found");
        r.Focus("absent");r.Key(ImGuiKey.Enter);r.Frames();Assert.Null(r.Launcher.Workspace);
        var row=r.Row("absent");var id=row.Id;r.Host.Click(r.Launcher.Draw,row.Bounds.X+10,row.Bounds.Y+row.Bounds.Height/2);r.Host.Click(r.Launcher.Draw,row.Bounds.X+10,row.Bounds.Y+row.Bounds.Height/2);r.Frames();
        Assert.Null(r.Launcher.Workspace);Assert.Equal(0,r.Probe.Calls);Assert.Equal(history,File.ReadAllBytes(r.Launcher.Recent.File));Assert.Equal(id,r.Row("absent").Id);
        Assert.False(r.Launcher.OpenRecent(absent));Assert.Contains("Remove it from the list",r.Launcher.Said);Assert.Equal(0,r.Probe.Calls);
        r.Context("absent");var open=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Open");Assert.False(open.Enabled);Assert.Contains("not found",open.DisabledReason);
        r.Action("Remove from list");Assert.Equal(keep,Assert.Single(new RecentProjects(r.Launcher.Recent.File).Items));
        Assert.Equal(source,File.ReadAllBytes(Path.Combine(keep.Data,"item.json")));Assert.False(Directory.Exists(Path.GetDirectoryName(absent.Data)));Assert.Equal(0,r.Probe.Calls);
    }
    private static JsonObject Entry(string file,ProjectPaths project)
        =>JsonNode.Parse(File.ReadAllText(file))!.AsArray().Select(n=>n!.AsObject()).Single(n=>n["Data"]!.GetValue<string>()==project.Data);

    [Fact]
    public void Native_pin_and_unpin_keep_row_identity_and_project_bytes_and_survive_MRU_eviction_and_reload()
    {
        using var r=new Rig(2);var alpha=r.Add("alpha");var beta=r.Add("beta");r.Frames();var id=r.Row("alpha").Id;
        var before=new[]{alpha,beta}.SelectMany(p=>Directory.GetFiles(Path.GetDirectoryName(p.Data)!,"*",SearchOption.AllDirectories)).ToDictionary(p=>p,File.ReadAllBytes);
        var staleOtherWriter=new RecentProjects(r.Launcher.Recent.File,2);
        r.Context("alpha");r.Action("Pin");
        Assert.Equal(id,r.Row("alpha").Id);Assert.True(r.Row("alpha").Selected);Assert.Equal(alpha,r.Launcher.Recent.Items[0]);Assert.True(Entry(r.Launcher.Recent.File,alpha)["Pinned"]!.GetValue<bool>());
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Pinned");Assert.Null(r.Launcher.Recent.Metadata(alpha).LastOpenedUtc);
        var gamma=r.Add("gamma");Assert.Equal(new[]{alpha,gamma},new RecentProjects(r.Launcher.Recent.File,2).Items);
        // A writer constructed before the pin must merge the current disk pin, never resurrect/evict it.
        staleOtherWriter.Add(beta);Assert.Equal(new[]{alpha,beta},new RecentProjects(r.Launcher.Recent.File,2).Items);
        r.Context("alpha");r.Action("Unpin");Assert.Equal(id,r.Row("alpha").Id);
        Assert.Null(Entry(r.Launcher.Recent.File,alpha)["Pinned"]);Assert.Equal(new[]{beta,alpha},new RecentProjects(r.Launcher.Recent.File,2).Items);
        foreach(var(path,bytes)in before)Assert.Equal(bytes,File.ReadAllBytes(path));Assert.Equal(0,r.Probe.Calls);Assert.Null(r.Launcher.Workspace);
    }
    [Fact]
    public void All_pinned_capacity_preserves_every_pin_and_successful_open_explains_why_it_was_not_remembered()
    {
        using var r=new Rig(2);var alpha=r.Add("alpha");var beta=r.Add("beta");r.Frames();
        r.Context("alpha");r.Action("Pin");r.Context("beta");r.Action("Pin");
        var third=r.Add("third");Assert.Equal(2,new RecentProjects(r.Launcher.Recent.File,2).Items.Count);Assert.DoesNotContain(r.Launcher.Recent.Items,p=>p.Data==third.Data);
        r.Frames();Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label.Contains("All recent slots are pinned",StringComparison.Ordinal));
        Assert.True(r.Launcher.Open(third),r.Launcher.Said);Assert.Contains("not remembered",r.Launcher.Said);Assert.Contains("pinned",r.Launcher.Workspace!.Said);
        var reloaded=new RecentProjects(r.Launcher.Recent.File,2);Assert.Equal(new[]{beta,alpha},reloaded.Items);Assert.True(Entry(reloaded.File,alpha)["Pinned"]!.GetValue<bool>());Assert.True(Entry(reloaded.File,beta)["Pinned"]!.GetValue<bool>());
        Assert.True(r.Launcher.Close());r.Frames();r.Context("beta");r.Action("Unpin");r.Launcher.Recent.Add(third);
        Assert.Equal(new[]{alpha,third},new RecentProjects(reloaded.File,2).Items);
        Assert.Equal("{}",File.ReadAllText(Path.Combine(third.Data,"item.json")));
    }
    [Theory][InlineData("null")][InlineData("false")][InlineData("17")][InlineData("\"yes\"")]
    public void Invalid_or_false_pin_metadata_loads_as_unpinned_and_is_omitted_on_write(string flag)
    {
        using var r=new Rig();var alpha=r.Add("alpha");var beta=r.Add("beta");
        var array=JsonNode.Parse(File.ReadAllText(r.Launcher.Recent.File))!.AsArray();array[1]!["Pinned"]=JsonNode.Parse(flag);File.WriteAllText(r.Launcher.Recent.File,array.ToJsonString());
        var reloaded=new RecentProjects(r.Launcher.Recent.File);Assert.Equal(new[]{beta,alpha},reloaded.Items);reloaded.Add(alpha);Assert.Equal(new[]{alpha,beta},reloaded.Items);
        Assert.All(JsonNode.Parse(File.ReadAllText(reloaded.File))!.AsArray(),n=>Assert.Null(n!["Pinned"]));
    }

    [Fact]
    public void Successful_reopen_preserves_a_pin_while_recording_real_open_metadata()
    {
        using var r=new Rig();var alpha=r.Add("alpha");r.Add("beta");r.Frames();r.Context("alpha");r.Action("Pin");
        Assert.True(r.Launcher.OpenRecent(alpha),r.Launcher.Said);
        var stored=new RecentProjects(r.Launcher.Recent.File);Assert.True(Entry(stored.File,alpha)["Pinned"]!.GetValue<bool>());
        Assert.NotNull(stored.Metadata(alpha).LastOpenedUtc);Assert.Equal("Data only",stored.Metadata(alpha).Kind);Assert.Equal(alpha.Data,stored.Items[0].Data);
        Assert.Equal("{}",File.ReadAllText(Path.Combine(alpha.Data,"item.json")));
    }

}
