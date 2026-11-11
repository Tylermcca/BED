using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Editor;
using Xunit;
namespace Bed.Tests;

public sealed class Scene2BuildTests:IDisposable
{
    private readonly string root=Directory.CreateTempSubdirectory("scene2-build-").FullName;
    private const string Source="data/level.json",Generated=".bed/generated/scenes/level.json",Stamp=".bed/generated/scenes/build.stamp.json";
    private string Put(string name,string text){var path=Path.Combine(root,name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,text);return path;}
    public void Dispose()=>Directory.Delete(root,true);
    [Fact]
    public void Settings_are_owned_explicit_and_preserve_other_fields()
    {
        SceneSpace[] spaces=[SceneSpace.ThreeD,SceneSpace.TwoDXY];var value=new Scene2BuildSettings(Source,Generated,spaces);spaces[0]=SceneSpace.TwoDXY;
        Assert.Equal(SceneSpace.ThreeD,value.Spaces[0]);Assert.Null(Scene2BuildSettings.ReadProject("{}"));
        var json=Scene2BuildSettings.WriteProject("""{"theme":"dark","play":{"custom":42}}""",value);
        using var doc=JsonDocument.Parse(json);Assert.Equal(42,doc.RootElement.GetProperty("play").GetProperty("custom").GetInt32());
        Assert.Equal("dark",doc.RootElement.GetProperty("theme").GetString());var again=Scene2BuildSettings.ReadProject(json)!;
        Assert.Equal(Source,again.Source);Assert.Equal(Generated,again.Generated);Assert.Equal(value.Spaces,again.Spaces);
    }
    [Theory]
    [InlineData("../level.json")][InlineData("/level.json")][InlineData("data/x:ads")]
    [InlineData("data/./level.json")][InlineData("data//level.json")][InlineData("data/CON.json")][InlineData("data/x. /level.json")][InlineData(".bed/source.json")][InlineData("data/<bad>.json")]
    public void Unsafe_source_paths_refuse(string source)=>Assert.Throws<ArgumentException>(()=>new Scene2BuildSettings(source,Generated,[SceneSpace.ThreeD]));
    [Fact]
    public void Platform_absolute_source_path_refuses()
        =>Assert.Throws<ArgumentException>(()=>new Scene2BuildSettings(Path.GetFullPath(Path.Combine(root,"level.json")).Replace('\\','/'),Generated,[SceneSpace.ThreeD]));
    [Theory]
    [InlineData("version","2")][InlineData("sceneVersion","1")][InlineData("spaces","[]")][InlineData("spaces","[\"3d\",\"3d\"]")][InlineData("spaces","[\"2d-xz\"]")][InlineData("generated","\"data/generated.json\"")][InlineData("unknown","true")]
    public void Unsupported_or_ambiguous_capability_refuses(string field,string json)
    {
        var settings=JsonNode.Parse(Scene2BuildSettings.WriteProject("{}",new(Source,Generated,[SceneSpace.ThreeD])))!;
        settings["sceneBuild"]![field]=JsonNode.Parse(json);Assert.Throws<DocumentFormatException>(()=>Scene2BuildSettings.ReadProject(settings.ToJsonString()));
        Assert.Throws<DocumentFormatException>(()=>Scene2BuildSettings.ReadProject("""{"sceneBuild":{},"sceneBuild":{}}"""));
    }
    [Fact]
    public void Stamp_uses_contents_even_when_input_and_output_mtimes_do_not_change()
    {
        var input=Put("input.txt","one");var output=Put("output.txt","built");var it=File.GetLastWriteTimeUtc(input);var ot=File.GetLastWriteTimeUtc(output);
        var fingerprint=ProjectBuildStamp.Fingerprint(root,["input.txt"]);ProjectBuildStamp.Write(root,"stamp.json",["input.txt"],fingerprint,["output.txt"]);
        Assert.True(ProjectBuildStamp.Current(root,"stamp.json",["input.txt"],out var why),why);
        File.WriteAllText(input,"two");File.SetLastWriteTimeUtc(input,it);Assert.False(ProjectBuildStamp.Current(root,"stamp.json",["input.txt"],out why));Assert.Contains("input",why);
        File.WriteAllText(input,"one");File.SetLastWriteTimeUtc(input,it);File.WriteAllText(output,"other");File.SetLastWriteTimeUtc(output,ot);
        Assert.False(ProjectBuildStamp.Current(root,"stamp.json",["input.txt"],out why));Assert.Contains("output",why);
        Assert.Throws<IOException>(()=>ProjectBuildStamp.Write(root,"stamp.json",["input.txt"],new string('0',64),["output.txt"]));
    }
    [Fact]
    public void Fingerprint_is_root_independent_and_glob_order_independent()
    {
        Put("data/a.json","a");Put("data/sub/b.json","b");var a=ProjectBuildStamp.Fingerprint(root,["data/**/*.json","data/a.json"]);
        Assert.Equal(a,ProjectBuildStamp.Fingerprint(root,["data/a.json","data/**/*.json"]));
        var copy=Directory.CreateTempSubdirectory("stamp-copy-").FullName;
        try{Directory.CreateDirectory(Path.Combine(copy,"data/sub"));File.Copy(Path.Combine(root,"data/a.json"),Path.Combine(copy,"data/a.json"));File.Copy(Path.Combine(root,"data/sub/b.json"),Path.Combine(copy,"data/sub/b.json"));Assert.Equal(a,ProjectBuildStamp.Fingerprint(copy,["data/**/*.json"]));}
        finally{Directory.Delete(copy,true);}
    }
    [Fact]
    public void Missing_corrupt_self_referential_and_over_budget_stamps_refuse_without_writing_inputs()
    {
        Put("input.txt","keep");Put("output.txt","built");var hash=ProjectBuildStamp.Fingerprint(root,["input.txt"]);
        Assert.False(ProjectBuildStamp.Current(root,"stamp.json",["input.txt"],out _));
        Assert.Throws<IOException>(()=>ProjectBuildStamp.Fingerprint(root,["missing.txt"]));
        Assert.Throws<ArgumentException>(()=>ProjectBuildStamp.Fingerprint(root,["../input.txt"]));
        Assert.Throws<ArgumentException>(()=>ProjectBuildStamp.Write(root,"stamp.json",["*.json"],hash,["output.txt"]));
        Assert.Throws<ArgumentException>(()=>ProjectBuildStamp.Write(root,"stamp.json",["input.txt"],hash,["stamp.json"]));
        Put("stamp.json","""{"version":1,"version":1}""");Assert.False(ProjectBuildStamp.Current(root,"stamp.json",["input.txt"],out _));
        using(var large=new FileStream(Path.Combine(root,"large.bin"),FileMode.Create,FileAccess.Write))large.SetLength(ProjectBuildStamp.MaxTotalBytes+1);
        Assert.Throws<IOException>(()=>ProjectBuildStamp.Fingerprint(root,["large.bin"]));Assert.Equal("keep",File.ReadAllText(Path.Combine(root,"input.txt")));
    }
    [Fact]
    public void Stamp_settings_roundtrip_inherit_clear_and_resolve_without_approving_execution()
    {
        var defaults=new PlaySettings("game.exe",build:new("./game.exe",inputs:[Source]){Stamp=Stamp});
        var settings=PlaySettings.ReadProject("""{"play":{"build":{"arguments":["build"]}}}""",defaults)!;
        Assert.Equal(Stamp,settings.Build!.Stamp);var text=PlaySettings.WriteProject("""{"other":42}""",settings);
        Assert.Equal(Stamp,PlaySettings.ReadProject(text)!.Build!.Stamp);
        Assert.Null(PlaySettings.ReadProject("""{"play":{"build":{"stamp":""}}}""",defaults)!.Build!.Stamp);
        Assert.True(PlaySettingsResolver.TryResolve(root,settings,out var launch,out var why),why);Assert.Equal(Path.Combine(root,Stamp.Replace('/',Path.DirectorySeparatorChar)),launch!.BuildStamp);
        Assert.False(PlaySettingsResolver.TryResolve(root,new("game.exe",build:new("./game.exe"){Stamp="../outside"}),out _,out _));
    }
    [Theory]
    [InlineData("3d")][InlineData("2d-xy")]
    public void Sample_generator_writes_only_generated_files_and_stamp_and_rechecks_saved_content(string space)
    {
        var source="{\"kind\":\"scene\",\"version\":2,\"space\":\""+space+"\",\"objects\":[{\"id\":\"root\",\"position\":[1,2,0]}]}";
        Put(Source,source);var config=Scene2BuildSettings.WriteProject("{}",new(Source,Generated,[SceneSpace.ThreeD,SceneSpace.TwoDXY]));Put(".bed/project.json",config);
        using(var publisher=new AssetCatalogPublisher(root)){var stage=publisher.Prepare("empty-self-authored",new Dictionary<string,byte[]>());Assert.True(publisher.Publish(stage,out _));}
        using var output=new StringWriter();using var error=new StringWriter();Assert.Equal(0,SceneStarter.Program.Run(["--compile-scene2",root],output,error));Assert.Equal("",error.ToString());
        var flat=SceneRuntimeReader.Read2(File.ReadAllText(Path.Combine(root,Generated))).Scene;Assert.Empty(flat.Bases);Assert.Empty(flat.Instances);Assert.Equal("root",Assert.Single(flat.Objects).Object.Id);
        Assert.True(ProjectBuildStamp.Current(root,Stamp,[Source,".bed/project.json",".bed/cache/current.json"],out var why),why);
        Assert.Equal(source,File.ReadAllText(Path.Combine(root,Source)));Assert.Equal(config,File.ReadAllText(Path.Combine(root,".bed/project.json")));
        var before=File.GetLastWriteTimeUtc(Path.Combine(root,Source));Put(Source,source.Replace("[1,2,0]","[2,2,0]"));File.SetLastWriteTimeUtc(Path.Combine(root,Source),before);
        Assert.False(ProjectBuildStamp.Current(root,Stamp,[Source,".bed/project.json",".bed/cache/current.json"],out _));
        Assert.Equal(0,SceneStarter.Program.Run(["--compile-scene2",root],output,error));Assert.True(ProjectBuildStamp.Current(root,Stamp,[Source,".bed/project.json",".bed/cache/current.json"],out why),why);
    }

    [Fact]
    public void Accepted_prefab_compiles_without_external_definitions_and_later_unaccepted_files_do_not_invalidate_it()
    {
        var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent;
        var source=File.ReadAllText(Path.Combine(repo!.FullName,"tests/Bed.Tests/Fixtures/Scene2/scene-3d.example.json"));Put(Source,source);
        Put(".bed/project.json",Scene2BuildSettings.WriteProject("{}",new(Source,Generated,[SceneSpace.ThreeD])));
        using(var publisher=new AssetCatalogPublisher(root)){var stage=publisher.Prepare("empty",new Dictionary<string,byte[]>());Assert.True(publisher.Publish(stage,out _));}
        using var output=new StringWriter();using var error=new StringWriter();Assert.Equal(0,SceneStarter.Program.Run(["--compile-scene2",root],output,error));Assert.Empty(error.ToString());
        var expected=Scene2Resolver.Resolve(new Scene2Format().Read(source));var flat=SceneRuntimeReader.Read2(File.ReadAllText(Path.Combine(root,Generated))).Scene;
        Assert.Equal(expected.Objects.Select(x=>JsonSerializer.Serialize(x)),flat.Objects.Select(x=>JsonSerializer.Serialize(x)));
        Put("data/definitions/unaccepted.json","malformed unaccepted source");
        Assert.True(ProjectBuildStamp.Current(root,Stamp,[Source,".bed/project.json",".bed/cache/current.json"],out var why),why);
    }
    [Theory]
    [InlineData("space")][InlineData("missing-generation")][InlineData("missing-asset")][InlineData("bad-inputs")]
    public void Invalid_saved_generation_refuses_before_publishing_outputs(string reason)
    {
        var item=reason=="missing-asset"?",\"asset\":\"11111111111111111111111111111111\"":"";
        var source="{\"kind\":\"scene\",\"version\":2,\"space\":\"3d\",\"objects\":[{\"id\":\"root\""+item+"}]}";Put(Source,source);
        var config=Scene2BuildSettings.WriteProject(reason=="bad-inputs"?"""{"play":{"build":{"inputs":[]}}}""":"{}",new(Source,Generated,[reason=="space"?SceneSpace.TwoDXY:SceneSpace.ThreeD]));Put(".bed/project.json",config);
        if(reason!="missing-generation")using(var publisher=new AssetCatalogPublisher(root)){var stage=publisher.Prepare("empty",new Dictionary<string,byte[]>());Assert.True(publisher.Publish(stage,out _));}
        using var output=new StringWriter();using var error=new StringWriter();Assert.Equal(3,SceneStarter.Program.Run(["--compile-scene2",root],output,error));Assert.NotEmpty(error.ToString());
        Assert.False(File.Exists(Path.Combine(root,Generated)));Assert.False(File.Exists(Path.Combine(root,Stamp)));Assert.Equal(source,File.ReadAllText(Path.Combine(root,Source)));
    }
    [Fact]
    public void Sample_adapter_admits_the_declared_source_and_keeps_generated_files_outside_the_data_host()
    {
        Put("data/custom.json","""{"kind":"scene","version":2,"space":"3d","objects":[]}""");Directory.CreateDirectory(Path.Combine(root,"schemas"));
        Put(".bed/project.json",Scene2BuildSettings.WriteProject("{}",new("data/custom.json",Generated,[SceneSpace.ThreeD])));
        var loaded=Bed.Editor.ImGui.Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters","SceneStarterAdapter.dll")]);Assert.Empty(loaded.Problems);var adapter=Assert.Single(loaded.Loaded);
        var paths=new Bed.Editor.ImGui.ProjectPaths(Path.Combine(root,"data"),Path.Combine(root,"schemas")){Root=root};
        var data=adapter.Open(paths);Assert.NotNull(data.Find("custom.json"));Assert.NotEmpty(data.FileAccessRefusal("../"+Generated)!);Assert.True(DocumentHost.For(data).OpenScene2("custom.json",out _,out var why),why);
        var settings=adapter.DefaultPlay(paths)!;Assert.Equal(Stamp,settings.Build!.Stamp);Assert.Equal(new[]{"--compile-scene2","."},settings.Build.Arguments);Assert.Contains("data/custom.json",settings.Build.Inputs);
        Put(".bed/project.json",Scene2BuildSettings.WriteProject("{}",new("outside.json",Generated,[SceneSpace.ThreeD])));Assert.Throws<InvalidOperationException>(()=>adapter.Open(paths));
    }

    [Fact]
    public void Sprite_generation_checks_the_published_texture_payload_kind()
    {
        const string id="11111111111111111111111111111111";
        Put(Source,"""{"kind":"scene","version":2,"space":"2d-xy","objects":[{"id":"sign","asset":"11111111111111111111111111111111","sprite":{"size":[2,3],"pivot":[0,1],"order":4,"filter":"nearest"}}]}""");
        Put(".bed/project.json",Scene2BuildSettings.WriteProject("{}",new(Source,Generated,[SceneSpace.TwoDXY])));
        using var publisher=new AssetCatalogPublisher(root);
        var wrong=DerivedMeshCodec.Encode(new([System.Numerics.Vector3.Zero,System.Numerics.Vector3.UnitX,System.Numerics.Vector3.UnitY],[0,1,2]));
        Assert.True(publisher.Publish(publisher.Prepare("wrong-kind",new Dictionary<string,byte[]>{{id+".bin",wrong}}),out _));
        using var output=new StringWriter();using var error=new StringWriter();Assert.Equal(3,SceneStarter.Program.Run(["--compile-scene2",root],output,error));Assert.False(File.Exists(Path.Combine(root,Stamp)));
        var texture=new byte[20];"BEDTEX1\0"u8.CopyTo(texture);System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(texture.AsSpan(8),1);System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(texture.AsSpan(12),1);texture[16]=47;texture[19]=127;
        Assert.True(publisher.Publish(publisher.Prepare("valid-self-authored-pixel",new Dictionary<string,byte[]>{{id+".bin",texture}}),out _));
        error.GetStringBuilder().Clear();Assert.Equal(0,SceneStarter.Program.Run(["--compile-scene2",root],output,error));Assert.Empty(error.ToString());
        var item=Assert.Single(SceneRuntimeReader.Read2(File.ReadAllText(Path.Combine(root,Generated))).Scene.Objects);Assert.Equal(4,item.Sprite!.Order);Assert.Equal(SceneSpriteFilter.Nearest,item.Sprite.Filter);
    }
}
