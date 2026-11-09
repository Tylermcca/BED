using Brutal.Authoring.Data;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;

namespace Brutal.Authoring.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class FileContextMenuTests
{
    private sealed class Rig : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "file-context-" + Guid.NewGuid().ToString("N"));
        public HeadlessHost Host { get; } = new(1200, 800) { Drawing = false };
        public DataWorkspace Workspace { get; }
        public Rig(bool readOnly = false, bool show = true)
        {
            Directory.CreateDirectory(Path.Combine(Root, "data")); Directory.CreateDirectory(Path.Combine(Root, "schemas"));
            foreach (var name in new[] { "alpha", "beta" })
            {
                File.WriteAllText(Path.Combine(Root,"schemas",name+".schema.json"), """{"type":"object","properties":{"value":{"type":"integer"}}}""");
                File.WriteAllText(Path.Combine(Root,"data",name+".json"), """{"value":1}""");
            }
            Workspace = new(DataProject.Open(Path.Combine(Root,"data"), Path.Combine(Root,"schemas"), readOnly));
            if (show) Assert.True(Workspace.Show("alpha.json"));
            EditorWindow<DataDocument>.Configure(ImGui.GetIO()); Host.Frames(Workspace.Draw, 5);
        }
        public UiNode Node(string text, UiRole role) => Assert.Single(Ui.Snapshot().Nodes, n => n.Role == role && n.Label.Trim() == text);
        public void Click(string text, UiRole role)
        { var b=Node(text,role).Bounds; Host.Click(Workspace.Draw,b.X+b.Width/2,b.Y+b.Height/2);Host.Frames(Workspace.Draw,3); }
        public void Open(string route)
        {
            if(route=="right")
            {
                var b=Node("beta.json",UiRole.ListItem).Bounds;
                Host.MoveMouse(Workspace.Draw,b.X+b.Width/2,b.Y+b.Height/2);
                ImGui.GetIO().AddMouseButtonEvent(1,true);Host.Frame(Workspace.Draw);
                ImGui.GetIO().AddMouseButtonEvent(1,false);Host.Frames(Workspace.Draw,3);
            }
            else
            { Click("beta.json",UiRole.ListItem);Host.Press(Workspace.Draw,route=="menu"?ImGuiKey.Menu:ImGuiKey.F10,shift:route=="shift");Host.Frames(Workspace.Draw,3); }
        }
        public void Dispose(){ Workspace.Dispose(); Host.Dispose(); Directory.Delete(Root,true); }
    }

    [Theory]
    [InlineData("right")][InlineData("menu")][InlineData("shift")]
    public void Target_is_opened_before_actions_and_copy_path_uses_that_target(string route)
    {
        using var r=new Rig(); r.Open(route);
        Assert.Equal("beta.json",r.Workspace.Active!.Name);
        foreach(var label in new[]{"Open","Rename","Delete","Restore","Show in Explorer","Copy path"})
        { var n=r.Node(label,UiRole.MenuItem);if(label is "Restore" or "Show in Explorer"){Assert.False(n.Enabled);Assert.NotEmpty(n.DisabledReason!);} }
        r.Click("Copy path",UiRole.MenuItem);
        Assert.Equal(Path.Combine(r.Root,"data","beta.json"),ImGui.GetClipboardText().ToString());
        Assert.Empty(r.Workspace.Project.Dirty);
        Assert.Equal("{\"value\":1}",File.ReadAllText(Path.Combine(r.Root,"data","beta.json")));
    }

    [Theory]
    [InlineData("Rename","Rename it")][InlineData("Delete","Delete it")]
    public void Mutating_items_open_existing_confirmation_and_cancel_preserves_files(string item,string confirmation)
    {
        using var r=new Rig();r.Open("right");r.Click(item,UiRole.MenuItem);
        Assert.True(r.Node(confirmation,UiRole.Button).Enabled);
        r.Host.Press(r.Workspace.Draw,ImGuiKey.Escape);r.Host.Frames(r.Workspace.Draw,2);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label==confirmation);
        Assert.Equal("beta.json",r.Workspace.Active!.Name);Assert.Empty(r.Workspace.Project.Dirty);
        foreach(var name in new[]{"alpha","beta"})Assert.Equal("{\"value\":1}",File.ReadAllText(Path.Combine(r.Root,"data",name+".json")));
    }

    [Fact]
    public void First_context_open_keeps_popup_when_workspace_opens_its_first_editor()
    {
        using var r=new Rig(show:false);r.Open("right");
        Assert.Equal("beta.json",r.Workspace.Active!.Name);
        Assert.True(r.Node("Copy path",UiRole.MenuItem).Enabled);
    }

    [Fact]
    public void Read_only_file_keeps_mutations_disabled_with_reason()
    {
        using var r=new Rig(true);r.Open("right");
        foreach(var label in new[]{"Rename","Delete"}){var n=r.Node(label,UiRole.MenuItem);Assert.False(n.Enabled);Assert.Contains("read-only",n.DisabledReason);}
        Assert.Empty(r.Workspace.Project.Dirty);
    }
}

