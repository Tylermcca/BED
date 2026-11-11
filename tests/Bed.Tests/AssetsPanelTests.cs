using Bed.Data;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class AssetsPanelTests
{
    private sealed class Rig : IDisposable
    {
        public readonly string Root = Directory.CreateTempSubdirectory("assets-panel-").FullName;
        public HeadlessHost Host { get; } = new(1000, 800) { Drawing = false };
        public AssetBrowser Browser { get; }
        public AssetsPanel Panel { get; }
        public Rig(bool empty=false)
        {
            if(!empty)
            {
                Add("lamp.txt", "11111111111111111111111111111111", true);
                Add("missing.txt", "22222222222222222222222222222222", false);
                File.WriteAllText(Path.Combine(Root,"new.txt"),"synthetic unimported content");
            }
            Browser=new(Root,".txt"); Panel=new(Browser);
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());Host.Frames(Draw,3);
        }
        private void Add(string name,string id,bool exists)
        {
            var path=Path.Combine(Root,name);File.WriteAllText(path,"synthetic content");
            var record=new AssetRecord(id,name,AssetRecord.HashOf(path),"synthetic",1,JsonSerializer.SerializeToElement(new {}),new("fixture","self-authored","fixture",Declared:AssetOrigin.Ours),AiUse.Forbidden);
            File.WriteAllText(path+AssetRecord.Suffix,record.Write());if(!exists)File.Delete(path);
        }
        public void Draw(){ImGui.SetNextWindowPos(new float2(0,0));ImGui.SetNextWindowSize(new float2(950,750));Ui.Begin("Assets fixture"u8);Panel.Draw();Ui.End();}
        public UiNode Node(string label,UiRole role)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==label&&n.Role==role);
        public void Click(string label,UiRole role){var b=Node(label,role).Bounds;Host.Click(Draw,b.X+b.Width/2,b.Y+b.Height/2);Host.Frames(Draw,2);}
        public string Text=>string.Join("\n",Ui.Snapshot().Nodes.Select(n=>n.Label));
        public void Dispose(){Host.Dispose();Directory.Delete(Root,true);}
    }

    [Fact]
    public void Selection_shows_existing_metadata_without_inventing_type_or_ready_state()
    {
        using var r=new Rig();r.Click("lamp.txt",UiRole.ListItem);
        Assert.Equal("11111111111111111111111111111111",r.Panel.Selected!.Guid);
        Assert.Contains("Importer: synthetic",r.Text);Assert.Contains("File type: TXT",r.Text);
        Assert.Contains("AI use: Forbidden",r.Text);Assert.Contains("Source file found. Placement and export need separate checks.",r.Text);
        Assert.Contains("Not imported",r.Text);Assert.Contains("new.txt",r.Text);
    }
    [Fact]
    public void Search_and_problem_filter_use_existing_browser_and_keep_selection()
    {
        using var r=new Rig();r.Click("lamp.txt",UiRole.ListItem);r.Click("Search assets",UiRole.TextField);r.Host.Type(r.Draw,"missing");r.Host.Frames(r.Draw,2);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label=="lamp.txt");
        Assert.Equal("lamp.txt",r.Panel.Selected!.RelativePath);
        r.Click("Problems only",UiRole.Checkbox);r.Click("missing.txt (needs attention)",UiRole.ListItem);
        Assert.Contains("Source file unavailable: it is missing or shares an ID with another asset.",r.Text);Assert.Contains("has no asset beside it",r.Text);
    }
    [Fact]
    public void Removed_selection_is_not_silently_rebound_after_external_refresh()
    {
        using var r=new Rig();r.Click("lamp.txt",UiRole.ListItem);
        File.Delete(Path.Combine(r.Root,"lamp.txt.asset.json"));r.Browser.Refresh();r.Host.Frames(r.Draw,2);
        Assert.Null(r.Panel.Selected);Assert.Contains("selected asset is no longer listed",r.Text);
    }
    [Fact]
    public void Empty_browser_has_explicit_guidance_and_panel_identity()
    {
        using var r=new Rig(true);Assert.Contains("No imported assets.",r.Text);Assert.Contains("Import images or models for this project to begin.",r.Text);
        Assert.Equal("editor.assets",r.Panel.AsPanel().Id);
    }
}
