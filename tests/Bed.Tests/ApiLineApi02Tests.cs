using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Bed.Launcher;
using Xunit;
namespace Bed.Tests;

public sealed class ApiLineApi02Tests
{
    private sealed class Probe(PluginManifest manifest, Action register) : IEditorPlugin<Scene>
    {
        public PluginManifest Manifest => manifest;
        public void Register(PluginRegistrar<Scene> registrar) => register();
    }

    [Theory]
    [InlineData("0.1",false)]
    [InlineData("0.2",true)]
    [InlineData("0.2.1",false)]
    [InlineData("0.3",false)]
    [InlineData("1.0",false)]
    public void Default_host_admits_only_compatible_selected_API_line_before_registration(string required,bool accepted)
    {
        Assert.Equal(new ApiVersion(0,2,0),AuthoringInfo.ApiVersion);
        var manifest=PluginManifest.Read(JsonSerializer.Serialize(new{id="probe",name="Probe",version="1.0",api=required,license="MIT"}));
        Assert.Equal(ApiVersion.Parse(required),manifest.Api);
        var editor=new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),Scene.Empty),SceneEditing.Outline);
        var host=new PluginHost<Scene>(editor,new Inspector<Scene>(editor),new Shortcuts());
        Assert.Equal(AuthoringInfo.ApiVersion,host.Api);var calls=0;
        Assert.Equal(accepted,host.Add(new Probe(manifest,()=>calls++)));
        Assert.Equal(accepted?1:0,calls);Assert.Equal(accepted,Assert.Single(host.Plugins).Enabled);
        if(!accepted)Assert.NotEmpty(Assert.Single(host.Plugins).Issues);
    }

    [Fact]
    public void Renderer_staging_and_probe_implementations_are_internal_with_test_only_friend_access()
    {
        Type[] implementations=[typeof(PlayProbe),typeof(SceneIndexedGeometry),typeof(SceneIndexedGeometry.Vertex),typeof(SceneMeshResources<>),typeof(BuildInputStaging)];
        foreach(var type in implementations)
        {
            Assert.False(type.IsVisible);Assert.DoesNotContain(type,type.Assembly.GetExportedTypes());
            Assert.Equal(["Bed.Tests"],type.Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a=>a.AssemblyName));
        }
        Assert.True(typeof(GameBuildSpec).IsVisible);Assert.True(typeof(GameBuildReceipt).IsVisible);Assert.True(typeof(IGameBuildHost).IsVisible);
    }
    [Fact]
    public void Approved_flatten_and_runtime_reader_members_are_in_the_pure_core_API()
    {
        var flatten=typeof(Scene2Kit).GetMethod(nameof(Scene2Kit.Flatten),[typeof(Scene2)]);Assert.NotNull(flatten);Assert.Equal(typeof(Scene2),flatten.ReturnType);
        var read=typeof(SceneRuntimeReader).GetMethod(nameof(SceneRuntimeReader.Read2),[typeof(string),typeof(Func<string,bool>)]);Assert.NotNull(read);Assert.Equal(typeof(Scene2RuntimeReadResult),read.ReturnType);Assert.Null(read.GetParameters()[1].DefaultValue);
        Assert.False(typeof(Scene2Runtime).IsVisible);
        var xml=System.Xml.Linq.XDocument.Load(Path.ChangeExtension(typeof(Scene2).Assembly.Location,".xml"));
        Assert.Contains(xml.Descendants("member"),m=>(string?)m.Attribute("name")=="M:Bed.Scene2Kit.Flatten(Bed.Scene2)");
        Assert.Contains(xml.Descendants("member"),m=>((string?)m.Attribute("name"))?.StartsWith("M:Bed.SceneRuntimeReader.Read2(",StringComparison.Ordinal)==true);
        Assert.DoesNotContain(typeof(SceneRuntimeReader).Assembly.GetReferencedAssemblies(),a=>a.Name!.Contains("Editor",StringComparison.Ordinal)||a.Name.StartsWith("Brutal.ImGui",StringComparison.Ordinal));
    }
    [Fact]
    public void Approved_scene2_surface_is_visible_documented_and_internal_helpers_stay_private()
    {
        Type[] surface=[typeof(SceneSpace),typeof(SceneSpriteFilter),typeof(SceneSprite),typeof(Scene2Object),typeof(PrefabReference),typeof(PrefabNode),typeof(PrefabSlot),typeof(PrefabDefinition),typeof(PrefabBase),typeof(PrefabOverrideOperation),typeof(PrefabOverride),typeof(PrefabInstance),typeof(Scene2),typeof(Scene2OriginKind),typeof(Scene2Origin),typeof(Scene2Resolution),typeof(Scene2Format),typeof(PrefabDefinitionFormat),typeof(Scene2Validator),typeof(PrefabDefinitionValidator),typeof(Scene2Resolver),typeof(Scene2Kit),typeof(Scene2RuntimeReadResult),typeof(Scene2BuildSettings),typeof(Scene2EditPlan)];
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Bed.slnx")))root=root.Parent;Assert.NotNull(root);
        var guide=File.ReadAllText(Path.Combine(root.FullName,"docs/guide/api_overview.md"));
        var xml=System.Xml.Linq.XDocument.Load(Path.ChangeExtension(typeof(Scene2).Assembly.Location,".xml"));
        foreach(var type in surface)
        {
            Assert.True(type.IsVisible);Assert.Equal(typeof(Scene).Assembly,type.Assembly);
            Assert.Contains("| `"+type.Name+"` |",guide);
            Assert.Contains(xml.Descendants("member"),m=>(string?)m.Attribute("name")=="T:"+type.FullName);
        }
        var expected=surface.Select(t=>t.Name).Order(StringComparer.Ordinal);
        var actual=typeof(Scene2).Assembly.GetExportedTypes().Where(t=>t.Name.StartsWith("Scene2",StringComparison.Ordinal)||t.Name.StartsWith("Prefab",StringComparison.Ordinal)||t.Name is "SceneSpace" or "SceneSprite" or "SceneSpriteFilter").Select(t=>t.Name).Order(StringComparer.Ordinal);
        Assert.Equal(expected,actual);Assert.False(typeof(Scene2Jcs).IsVisible);Assert.False(typeof(SceneVersionDispatch).IsVisible);
    }

    [Fact]
    public void Approved_scene2_host_types_are_visible_documented_in_editor_and_listed()
    {
        Type[] types=[typeof(Bed.Editor.Scene2DocumentEntry),typeof(Bed.Editor.Scene2Editing),typeof(PrefabSourceInfo)];
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Bed.slnx")))root=root.Parent;
        var guide=File.ReadAllText(Path.Combine(root!.FullName,"docs/guide/api_overview.md"));
        foreach(var type in types)
        {
            Assert.True(type.IsVisible);Assert.Equal(typeof(Bed.Editor.DocumentHost).Assembly,type.Assembly);Assert.Contains("| `"+type.Name+"` |",guide);
            var xml=System.Xml.Linq.XDocument.Load(Path.ChangeExtension(type.Assembly.Location,".xml"));Assert.Contains(xml.Descendants("member"),m=>(string?)m.Attribute("name")=="T:"+type.FullName);
        }
    }

    [Fact]
    public void Approved_prefab_source_picker_members_are_public_read_only_and_documented()
    {
        var type=typeof(PrefabSourceInfo);Assert.NotNull(type.GetConstructor([typeof(string),typeof(Guid),typeof(SceneSpace)]));Assert.Null(type.GetProperty("Slots")!.SetMethod);
        Assert.Equal(typeof(bool),typeof(DocumentHost).GetMethod("ListPrefabSources")!.ReturnType);
        var xml=System.Xml.Linq.XDocument.Load(Path.ChangeExtension(type.Assembly.Location,".xml"));
        Assert.Contains(xml.Descendants("member"),m=>(string?)m.Attribute("name")=="P:Bed.Editor.PrefabSourceInfo.Slots");
        Assert.Contains(xml.Descendants("member"),m=>((string?)m.Attribute("name"))?.StartsWith("M:Bed.Editor.DocumentHost.ListPrefabSources(",StringComparison.Ordinal)==true);
    }

    [Fact]
    public void Approved_file_access_refusal_is_public_and_documented_on_DataProject()
    {
        var type=typeof(Bed.Data.DataProject);var method=type.GetMethod("FileAccessRefusal",[typeof(string)]);Assert.NotNull(method);Assert.Equal(typeof(string),method.ReturnType);
        var xml=System.Xml.Linq.XDocument.Load(Path.ChangeExtension(type.Assembly.Location,".xml"));
        Assert.Contains(xml.Descendants("member"),m=>(string?)m.Attribute("name")=="M:Bed.Data.DataProject.FileAccessRefusal(System.String)");
    }

    [Fact]
    public void Approved_sprite_asset_types_are_documented_in_their_framework_only_assemblies()
    {
        Type[] types=[typeof(Bed.Runtime.SpriteGeometry),typeof(SpriteAssetDefaults),typeof(SceneSpritePlacement),typeof(SpriteAssetClosure)];
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Bed.slnx")))root=root.Parent;
        var guide=File.ReadAllText(Path.Combine(root!.FullName,"docs/guide/api_overview.md"));
        foreach(var type in types)
        {
            Assert.True(type.IsVisible);Assert.Equal(type==types[0]?"Bed.Runtime":"Bed.Editor",type.Assembly.GetName().Name);
            Assert.Contains("| `"+type.Name+"` |",guide);
            var xml=System.Xml.Linq.XDocument.Load(Path.ChangeExtension(type.Assembly.Location,".xml"));
            Assert.Contains(xml.Descendants("member"),m=>(string?)m.Attribute("name")=="T:"+type.FullName);
            Assert.DoesNotContain(type.Assembly.GetReferencedAssemblies(),a=>a.Name!.StartsWith("Brutal.")&&!a.Name.StartsWith("Bed"));
        }
        Assert.NotNull(typeof(DocumentHost).GetMethod("CaptureSpritePlacement"));Assert.NotNull(typeof(DocumentHost).GetMethod("PlaceSprite"));
    }


    [Fact]
    public void Approved_content_stamp_members_are_visible_documented_and_framework_only()
    {
        var type=typeof(ProjectBuildStamp);Assert.True(type.IsVisible);
        var xml=System.Xml.Linq.XDocument.Load(Path.ChangeExtension(type.Assembly.Location,".xml"));
        foreach(var member in new[]{"T:Bed.ProjectBuildStamp","F:Bed.ProjectBuildStamp.MaxFiles","F:Bed.ProjectBuildStamp.MaxTotalBytes"})
            Assert.Contains(xml.Descendants("member"),m=>(string?)m.Attribute("name")==member);
        foreach(var name in new[]{"Fingerprint","Write","Current"})Assert.Single(type.GetMethods(),m=>m.DeclaringType==type&&m.Name==name);
        Assert.NotNull(typeof(PlayBuildSettings).GetConstructor([typeof(string),typeof(IEnumerable<string>),typeof(IEnumerable<string>)]));
        Assert.NotNull(typeof(PlayLaunch).GetConstructor([typeof(GameLaunchSpec),typeof(GameLaunchSpec),typeof(string),typeof(IReadOnlyList<string>)]));
        foreach(var owner in new[]{typeof(PlayBuildSettings),typeof(PlayLaunch)})
        {
            var prop=owner.GetProperty(owner==typeof(PlayLaunch)?"BuildStamp":"Stamp")!;
            Assert.True(prop.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)));
        }
    }

    [Fact]
    public void Approved_physics_values_are_framework_only_documented_and_extend_without_constructor_breaks()
    {
        Type[] types=[typeof(ScenePhysics),typeof(SceneBodyMotion),typeof(SceneShape),typeof(SceneBoxShape),typeof(SceneSphereShape),typeof(SceneCapsuleYShape),typeof(SceneMeshBoundsShape),typeof(SceneBody)];
        var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent;
        var guide=File.ReadAllText(Path.Combine(repo!.FullName,"docs/guide/api_overview.md"));var xml=System.Xml.Linq.XDocument.Load(Path.ChangeExtension(typeof(Scene2).Assembly.Location,".xml"));
        foreach(var type in types){Assert.True(type.IsVisible);Assert.Equal(typeof(Scene2).Assembly,type.Assembly);Assert.Contains("| `"+type.Name+"` |",guide);Assert.Contains(xml.Descendants("member"),m=>(string?)m.Attribute("name")=="T:"+type.FullName);}
        Assert.NotNull(typeof(Scene2Object).GetConstructor([typeof(SceneObject),typeof(SceneSprite)]));Assert.Single(typeof(Scene2).GetConstructors());Assert.Equal(5,typeof(Scene2).GetConstructors()[0].GetParameters().Length);Assert.Equal(10,Assert.Single(typeof(PrefabNode).GetConstructors()).GetParameters().Length);
        foreach(var owner in new[]{typeof(Scene2),typeof(Scene2Object),typeof(PrefabNode)}){var prop=owner.GetProperty(owner==typeof(Scene2)?"Physics":"Body")!;Assert.Contains(typeof(System.Runtime.CompilerServices.IsExternalInit),prop.SetMethod!.ReturnParameter.GetRequiredCustomModifiers());}
    }
}
