using System.Buffers.Binary;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Bed.Runtime;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;

namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class AssetThumbnailTests
{
    private const string TextureId = "11111111111111111111111111111111", ModelId = "22222222222222222222222222222222";
    private sealed class Rig : IDisposable
    {
        public readonly string Root = Directory.CreateTempSubdirectory("asset-thumbnail-").FullName;
        public readonly HeadlessHost Host = new(850, 780) { Drawing = false };
        public readonly AssetCatalog Catalog = new();
        public readonly AssetBrowser Browser;
        public readonly DataWorkspace Workspace;
        public readonly AssetCatalog.Input Input;
        public readonly string TexturePath;
        private readonly Action panelDraw;
        public Rig(int width = 256, int height = 128, bool artifact = false, bool corrupt = false)
        {
            var data = Directory.CreateDirectory(Path.Combine(Root, "data")).FullName;
            var schemas = Directory.CreateDirectory(Path.Combine(Root, "schemas")).FullName;
            var assets = Directory.CreateDirectory(Path.Combine(Root, "assets")).FullName;
            File.WriteAllText(Path.Combine(data, "level.json"), new SceneFormat().Write(Scene.Empty));
            var rgba = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var at = (y * width + x) * 4;
                    if (y < height / 2) rgba[at + (x < width / 2 ? 0 : 1)] = 255;
                    else rgba[at + 2] = 255;
                    rgba[at + 3] = y >= height / 2 && x >= width / 2 ? (byte)0 : (byte)255;
                }
            byte[] source;
            TexturePath = Path.Combine(assets, artifact ? "colour.bedtex" : "colour.tga");
            if (artifact)
            {
                source = new byte[16 + rgba.Length]; "BEDTEX1\0"u8.CopyTo(source);
                BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(8), (uint)width);
                BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(12), (uint)height);
                rgba.CopyTo(source, 16); if (corrupt) source[0] = 0;
            }
            else
            {
                source = new byte[18 + rgba.Length]; source[2] = 2; source[16] = 32; source[17] = 0x28;
                BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(12), (ushort)width);
                BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(14), (ushort)height);
                for (var i = 0; i < rgba.Length; i += 4)
                { source[18+i] = rgba[i+2]; source[19+i] = rgba[i+1]; source[20+i] = rgba[i]; source[21+i] = rgba[i+3]; }
            }
            File.WriteAllBytes(TexturePath, source);
            var record = Record(TextureId, TexturePath); File.WriteAllText(AssetRecord.SidecarFor(TexturePath), record.Write());
            if (artifact) Input = new(record, source, source, [], true, "BEDTEX1", record.Write());
            else
            {
                Assert.True(TextureAssetGeneration.TryCreateSavedInput(record, source, "tga", [], true, "BEDTEX1", record.Write(), out var input, out var why), why);
                Input = input!;
            }
            Publish();
            var model = Path.Combine(assets, "model.obj"); File.WriteAllText(model, "synthetic model metadata");
            File.WriteAllText(AssetRecord.SidecarFor(model), Record(ModelId, model).Write());
            Browser = new(assets);
            Workspace = new(DataProject.Open(data, schemas));
            Assert.True(Workspace.BindSceneAssets(Browser, Catalog));
            Assert.True(Workspace.Documents.BindScene("level.json", "scene", out _)); Assert.True(Workspace.Show("level.json"));
            panelDraw = Assert.Single(Workspace.SceneWindow!.Editor.Panels, p => p.Id == "scene.assets").Draw;
            EditorWindow<Scene>.Configure(ImGui.GetIO()); Host.Frames(Draw, 3); Select(Path.GetFileName(TexturePath));
        }
        private static AssetRecord Record(string id, string path) => new(id, Path.GetFileName(path), AssetRecord.HashOf(path), "fixture", 1,
            JsonSerializer.SerializeToElement(new { }), new("fixture", "self-authored", "test", Declared: AssetOrigin.Ours), AiUse.Forbidden);
        public void Publish() => Assert.True(Catalog.Publish(Catalog.BeginRefresh(), [Input], out var why), why);
        public void Draw()
        {
            ImGui.SetNextWindowPos(float2.Zero); ImGui.SetNextWindowSize(new float2(820, 750));
            Ui.Begin("Thumbnail fixture"u8); panelDraw(); Ui.End();
        }
        public void Select(string name)
        {
            var bounds = Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.ListItem && n.Label == name).Bounds;
            Host.Click(Draw, bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2); Host.Frames(Draw, 2);
        }
        public void PublishModel(int parts,bool corrupt=false)
        {
            var path=Path.Combine(Root,"assets","model.obj");File.WriteAllText(path,$"# self-authored {parts}-part fixture");
            var record=Record(ModelId,path);File.WriteAllText(AssetRecord.SidecarFor(path),record.Write());
            var mesh=new MeshData([System.Numerics.Vector3.Zero,System.Numerics.Vector3.UnitX,System.Numerics.Vector3.UnitY],[0,1,2]);
            var derived=corrupt?new byte[]{1,2,3}:parts==1?DerivedMeshCodec.Encode(mesh):DerivedMeshCodec.Encode(Enumerable.Range(0,parts).Select(i=>new MeshPart(mesh,new MeshPartMaterial(i,System.Numerics.Vector4.One,null))).ToArray());
            var model=new AssetCatalog.Input(record,File.ReadAllBytes(path),derived,[],true,parts==1?"BEDMESH1":"BEDMESH2",record.Write());
            Assert.True(Catalog.Publish(Catalog.BeginRefresh(),[Input,model],out var why),why);Browser.Refresh();Host.Frames(Draw,2);Select("model.obj");
        }
        public UiNode Thumbnail => Assert.Single(Ui.Snapshot().Nodes, n => n.Role == UiRole.Canvas && n.Label.StartsWith("Texture thumbnail:"));
        public string Text => string.Join("\n", Ui.Snapshot().Nodes.Select(n => n.Label));
        public uint Pixel(float x, float y) => Host.Pixels[(int)y * Host.Width + (int)x];
        public void Dispose() { Workspace.Dispose(); Catalog.Dispose(); Host.Dispose(); Directory.Delete(Root, true); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Selected_texture_has_bounded_tree_preview_and_the_saved_BEDTEX1_colours(bool artifact)
    {
        using var r = new Rig(artifact: artifact);
        var source = File.ReadAllBytes(r.TexturePath); var sidecar = File.ReadAllBytes(AssetRecord.SidecarFor(r.TexturePath));
        Assert.Contains("Size: 256 x 128 pixels", r.Text);
        var node = r.Thumbnail;var description=$"Texture thumbnail: {Path.GetFileName(r.TexturePath)}, 256 x 128 pixels";
        Assert.Equal(description, node.Label);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label==description);
        Assert.Equal(128, node.Bounds.Width); Assert.Equal(64, node.Bounds.Height);
        var stored = TextureAssetGeneration.StoredArtifact(r.Catalog.Resolve(TextureId).Current!);
        using (var publisher = new AssetCatalogPublisher(r.Root))
        {
            var stage = publisher.Prepare("fixture", new Dictionary<string, byte[]> { [TextureId + ".bin"] = stored });
            Assert.True(publisher.Publish(stage, out var why), why);
        }
        var saved = ProjectAssets.Open(r.Root).ReadTexture(Guid.ParseExact(TextureId, "N"));
        Assert.Equal(256, saved.Width); Assert.Equal(128, saved.Height);
        r.Host.Drawing = true; r.Host.Frame(r.Draw); var b = r.Thumbnail.Bounds;
        Assert.Equal(0xff0000ffu, r.Pixel(b.X + 16, b.Y + 8));
        Assert.Equal(0xff00ff00u, r.Pixel(b.X + 96, b.Y + 8));
        Assert.Equal(0xffff0000u, r.Pixel(b.X + 16, b.Y + 48));
        Assert.NotEqual(0xffff0000u, r.Pixel(b.X + 96, b.Y + 48)); // Transparent texels expose the checkerboard.
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, saved.Rgba[..4]);
        Assert.Equal(source, File.ReadAllBytes(r.TexturePath)); Assert.Equal(sidecar, File.ReadAllBytes(AssetRecord.SidecarFor(r.TexturePath)));
        r.Select("model.obj"); Assert.DoesNotContain(Ui.Snapshot().Nodes, n => n.Label.StartsWith("Texture thumbnail:"));
    }

    [Theory]
    [InlineData(1,"1 part")]
    [InlineData(2,"2 parts")]
    public void Admitted_model_details_expose_name_kind_and_exact_part_count_in_plain_tree_text(int parts,string count)
    {
        using var rig=new Rig();rig.PublishModel(parts);
        Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Text&&n.Label=="Model: model.obj, "+count+".");
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label.StartsWith("Texture thumbnail:",StringComparison.Ordinal));
        rig.Catalog.BeginRefresh();rig.Host.Frames(rig.Draw,2);
        Assert.DoesNotContain(Ui.Snapshot().Nodes,n=>n.Label=="Model: model.obj, "+count+".");
    }
    [Fact]
    public void Changed_model_generation_updates_the_part_count_and_invalid_bytes_never_invent_one()
    {
        using var rig=new Rig();rig.PublishModel(1);Assert.Contains("Model: model.obj, 1 part.",rig.Text);
        rig.PublishModel(3);Assert.Contains("Model: model.obj, 3 parts.",rig.Text);Assert.DoesNotContain("Model: model.obj, 1 part.",rig.Text);
        rig.PublishModel(2,corrupt:true);Assert.Contains("Model details unavailable:",rig.Text);Assert.DoesNotContain("Model: model.obj, 2 parts.",rig.Text);
    }

    [Theory]
    [InlineData("pending")] [InlineData("closed")] [InlineData("missing")] [InlineData("source")]
    public void Unavailable_texture_removes_the_old_preview_and_explains_why(string mode)
    {
        using var r = new Rig(); Assert.False(r.Thumbnail.Bounds.IsEmpty);
        switch (mode)
        {
            case "pending": r.Catalog.BeginRefresh(); break;
            case "closed": r.Catalog.Dispose(); break;
            case "missing": Assert.True(r.Catalog.Publish(r.Catalog.BeginRefresh(), [], out _)); break;
            case "source": File.Delete(r.TexturePath); r.Browser.Refresh(); break;
        }
        r.Host.Frames(r.Draw, 2);
        Assert.DoesNotContain(Ui.Snapshot().Nodes, n => n.Label.StartsWith("Texture thumbnail:"));
        Assert.Contains("Texture preview unavailable:", r.Text); Assert.DoesNotContain("Size: 256", r.Text);
        if (mode == "pending") { r.Publish(); r.Host.Frames(r.Draw, 2); Assert.False(r.Thumbnail.Bounds.IsEmpty); }
    }

    [Fact]
    public void Invalid_BEDTEX1_is_a_plain_refusal_without_a_canvas()
    {
        using var r = new Rig(artifact: true, corrupt: true);
        Assert.Contains("Texture preview unavailable: Invalid BEDTEX1 header or size.", r.Text);
        Assert.DoesNotContain(Ui.Snapshot().Nodes, n => n.Role == UiRole.Canvas);
    }

    [Theory]
    [InlineData(1, 1024, 1, 128)] [InlineData(1024, 1, 128, 1)] [InlineData(2, 2, 128, 128)]
    public void Extreme_aspect_ratio_and_small_images_stay_in_the_display_bound(int width, int height, int shownWidth, int shownHeight)
    {
        using var r = new Rig(width, height); var b = r.Thumbnail.Bounds;
        Assert.Equal(shownWidth, b.Width); Assert.Equal(shownHeight, b.Height);
        Assert.Contains($"Size: {width} x {height} pixels", r.Text);
    }
}
