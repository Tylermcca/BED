using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class AssetDetailsUi02Tests
{
    private sealed class Rig : IDisposable
    {
        public readonly string Root=Directory.CreateTempSubdirectory("asset-details-ui02-").FullName;
        public readonly HeadlessHost Host=new(1000,800){Drawing=false};
        public readonly AssetBrowser Browser;
        public readonly AssetsPanel Panel;
        public readonly string Path;
        public Rig(string name,string mode="found")
        {
            Path=System.IO.Path.Combine(Root,name);
            File.WriteAllText(Path,name.EndsWith(".gltf",StringComparison.OrdinalIgnoreCase)?"""{"asset":{"version":"2.0"},"scenes":[{}]}""":"v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            var record=new AssetRecord(new string('a',32),name,AssetRecord.HashOf(Path),"synthetic",1,JsonSerializer.SerializeToElement(new {}),
                new("Fixture author","self-authored","script",Declared:AssetOrigin.Ours),AiUse.Forbidden);
            File.WriteAllText(Path+AssetRecord.Suffix,record.Write());
            if(mode=="missing")File.Delete(Path);
            if(mode=="changed")File.AppendAllText(Path,"# self-authored edit after import\n");
            if(mode=="duplicate")
            {
                var other=System.IO.Path.Combine(Root,"other.obj");File.Copy(Path,other);
                File.WriteAllText(other+AssetRecord.Suffix,(record with {Source="other.obj"}).Write());
            }
            Browser=new(Root);Panel=new(Browser);EditorWindow<DataDocument>.Configure(ImGui.GetIO());Host.Frames(Draw,3);
            var label=name+(Browser.Entries.Single(e=>e.RelativePath==name).HasProblems?" (needs attention)":"");
            var node=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label==label);var bounds=node.Bounds;
            Host.Click(Draw,bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2);Host.Frames(Draw,2);
            Assert.Equal(name,Panel.Selected!.RelativePath);
        }
        public void Draw()
        {ImGui.SetNextWindowPos(float2.Zero);ImGui.SetNextWindowSize(new float2(950,750));Ui.Begin("Asset details UI02"u8);Panel.Draw();Ui.End();}
        public string[] Labels=>Ui.Snapshot().Nodes.Select(n=>n.Label).ToArray();
        public void Dispose(){Host.Dispose();Directory.Delete(Root,true);}
    }
    [Theory]
    [InlineData("model.obj","Model (OBJ)")]
    [InlineData("model.gltf","Model (glTF)")]
    [InlineData("custom.data","File type: DATA")]
    [InlineData("no-extension","File type: not specified")]
    public void Indexed_file_has_plain_type_and_does_not_claim_placement_or_export_admission(string name,string type)
    {
        using var rig=new Rig(name);var sidecar=File.ReadAllBytes(rig.Path+AssetRecord.Suffix);
        Assert.Contains(type,rig.Labels);Assert.Contains("Source file found. Placement and export need separate checks.",rig.Labels);
        Assert.Contains("Importer: synthetic",rig.Labels);Assert.Contains("Licence: self-authored",rig.Labels);Assert.Contains("AI use: Forbidden",rig.Labels);
        Assert.DoesNotContain("Ready to place",rig.Labels);Assert.NotNull(rig.Browser.Index.Resolve(rig.Panel.Selected!.Guid));
        rig.Host.Frames(rig.Draw,2);Assert.Equal(sidecar,File.ReadAllBytes(rig.Path+AssetRecord.Suffix));
    }
    [Fact]
    public void Changed_source_keeps_reimport_diagnostic_even_though_it_is_still_indexed()
    {
        using var rig=new Rig("model.obj","changed");
        Assert.NotNull(rig.Browser.Index.Resolve(rig.Panel.Selected!.Guid));Assert.True(rig.Panel.Selected.HasProblems);
        Assert.Contains("Source file found. Placement and export need separate checks.",rig.Labels);
        Assert.Contains(rig.Labels,label=>label.Contains("changed since it was imported; re-import it",StringComparison.Ordinal));
        Assert.DoesNotContain("Ready to place",rig.Labels);
    }
    [Theory][InlineData("missing")][InlineData("duplicate")]
    public void Unresolvable_source_keeps_refusal_and_its_exact_diagnostic(string mode)
    {
        using var rig=new Rig("model.obj",mode);Assert.Null(rig.Browser.Index.Resolve(rig.Panel.Selected!.Guid));
        Assert.Contains("Source file unavailable: it is missing or shares an ID with another asset. Check the problems below.",rig.Labels);
        Assert.DoesNotContain("Source file found. Placement and export need separate checks.",rig.Labels);Assert.DoesNotContain("Ready to place",rig.Labels);
        Assert.Contains(rig.Labels,label=>label.Contains(mode=="missing"?"has no asset beside it":"is used by",StringComparison.Ordinal));
        Assert.Equal(mode!="missing",File.Exists(rig.Path));
    }
}
