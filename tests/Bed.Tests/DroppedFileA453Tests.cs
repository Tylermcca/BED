using Bed.Data;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class DroppedFileA453Tests
{
    private sealed class Rig : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "dropped-copy-" + Guid.NewGuid().ToString("N"));
        public readonly HeadlessHost Host = new(1200, 800) { Drawing = false };
        public DataWorkspace Workspace { get; }
        public string Source => Path.Combine(Root, "input", "items.json");
        public string Destination => Path.Combine(Root, "data", "items.json");
        public Rig()
        {
            Directory.CreateDirectory(Path.Combine(Root,"input"));
            Directory.CreateDirectory(Path.Combine(Root,"data"));
            Directory.CreateDirectory(Path.Combine(Root,"schemas"));
            File.WriteAllText(Source, "{\"count\":2}");
            File.WriteAllText(Path.Combine(Root,"schemas","items.schema.json"), """{"type":"object","required":["count"],"properties":{"count":{"type":"integer","minimum":0}}}""");
            Workspace = new(DataProject.Open(Path.Combine(Root,"data"),Path.Combine(Root,"schemas")));
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            Host.Frames(Workspace.Draw, 3);
        }
        public void Drop()
        { Assert.True(Workspace.DropOnFiles([Source]), Workspace.Said); Host.Frames(Workspace.Draw, 2); }
        public void Click(string label)
        {
            var node = Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button && n.Label==label);
            var b=node.Bounds; Host.Click(Workspace.Draw,b.X+b.Width/2,b.Y+b.Height/2); Host.Frames(Workspace.Draw,2);
        }
        public void Dispose() { Workspace.Dispose(); Host.Dispose(); Directory.Delete(Root,true); }
    }

    [Fact]
    public void Drop_only_prompts_then_confirmation_copies_without_moving_source()
    {
        using var r=new Rig(); r.Drop();
        Assert.False(File.Exists(r.Destination));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Copy items.json into this project's data folder?");
        r.Click("Copy it");
        Assert.Equal("{\"count\":2}",File.ReadAllText(r.Source));
        Assert.True(File.Exists(r.Destination),r.Workspace.Said);
        Assert.Equal(File.ReadAllText(r.Source),File.ReadAllText(r.Destination));
        Assert.Equal("items.json",r.Workspace.Active!.Name);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label=="Copy it");
    }

    [Fact]
    public void Escape_cancels_and_pending_confirmation_cannot_be_replaced()
    {
        using var r=new Rig(); r.Drop();
        Assert.False(r.Workspace.DropOnFiles([r.Source]));
        r.Host.Press(r.Workspace.Draw,ImGuiKey.Escape); r.Host.Frames(r.Workspace.Draw,2);
        Assert.False(File.Exists(r.Destination));
        Assert.Equal("{\"count\":2}",File.ReadAllText(r.Source));
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label=="Copy it");
    }

    [Theory]
    [InlineData("clash")][InlineData("schema")][InlineData("check")][InlineData("missing")]
    public void Confirmation_rechecks_current_destination_rules_and_source(string failure)
    {
        using var r=new Rig(); r.Drop();
        if(failure=="clash") File.WriteAllText(r.Destination,"outside writer");
        if(failure=="schema") File.WriteAllText(r.Source,"{\"count\":-1}");
        if(failure=="check") r.Workspace.Project.FileCheck=_=>"copy denied by project";
        if(failure=="missing") File.Delete(r.Source);
        r.Click("Copy it");
        Assert.Null(r.Workspace.Active);
        Assert.NotEmpty(r.Workspace.Said);
        if(failure=="clash") { Assert.Contains("already",r.Workspace.Said); Assert.Equal("outside writer",File.ReadAllText(r.Destination)); }
        else Assert.False(File.Exists(r.Destination));
        if(failure=="schema") Assert.Contains("must be at least 0",r.Workspace.Said);
        if(failure=="check") Assert.Contains("copy denied by project",r.Workspace.Said);
        if(failure=="missing") Assert.Contains("can't be read",r.Workspace.Said);
        else Assert.True(File.Exists(r.Source));
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Label=="Cancel");
    }

    [Fact]
    public void Unsupported_drop_has_no_confirmation_or_disk_effect()
    {
        using var r=new Rig();
        Assert.False(r.Workspace.DropOnFiles([r.Root]));
        Assert.False(r.Workspace.DropOnFiles([r.Source,r.Source]));
        r.Host.Frames(r.Workspace.Draw,2);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label=="Copy it");
        Assert.False(File.Exists(r.Destination));
        Assert.True(File.Exists(r.Source));
    }
}
