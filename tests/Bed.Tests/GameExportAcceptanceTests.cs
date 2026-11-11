using System.Security.Cryptography;
using Bed.Editor;
using Bed.Editor.App;
using Xunit;
namespace Bed.Tests;

public sealed class GameExportAcceptanceTests
{
    [Theory]
    [InlineData("valid")][InlineData("missing")][InlineData("corrupt")][InlineData("version")][InlineData("transform")]
    public void Runtime_entry_is_fail_closed_and_never_repairs_the_sample(string mode)
    {
        var root=Directory.CreateTempSubdirectory("scene-smoke-").FullName;
        try
        {
            var path=Path.Combine(root,"scene.json");
            var text=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"SceneStarterReview","scene.json"));
            text=mode switch {"corrupt"=>"{", "version"=>text.Replace("\"version\":1","\"version\":99"),
                "transform"=>text.Replace("[1,0,0]","[9,0,0]"), _=>text};
            if(mode!="missing")File.WriteAllText(path,text);
            using var output=new StringWriter();using var error=new StringWriter();
            var code=SceneStarter.Program.Run(["--smoke",path],output,error);
            Assert.Equal(mode=="valid"?0:3,code);
            if(mode=="valid")Assert.Contains("smoke passed",output.ToString());else Assert.Contains("refused",error.ToString());
            if(mode=="missing")Assert.False(File.Exists(path));else Assert.Equal(text,File.ReadAllText(path));
        }
        finally{Directory.Delete(root,true);}
    }

    [Theory]
    [InlineData("valid")][InlineData("extra-editor")][InlineData("source")][InlineData("missing-notice")][InlineData("corrupt-scene")][InlineData("runtime-null")][InlineData("hidden-runtime")]
    public void Actual_build_bytes_are_strictly_reviewed_then_pinned_for_unchanged_staging(string mode)
    {
        var root=Directory.CreateTempSubdirectory("scene-review-").FullName;
        try
        {
            // Actual compiled runtime bytes, not a synthetic byte helper. This is a headless review fixture,
            // not an owner-run publish/process receipt or proof of the supervised child route.
            var built=Path.Combine(AppContext.BaseDirectory,"SceneStarterReview");
            foreach(var name in SceneStarter.ExportInventory.Required)File.Copy(Path.Combine(built,name),Path.Combine(root,name));
            if(mode=="extra-editor")File.Copy(Path.Combine(built,"Bed.dll"),Path.Combine(root,"Bed.Editor.dll"));
            if(mode=="source")File.WriteAllText(Path.Combine(root,"private.cs"),"self-authored forbidden fixture");
            if(mode=="missing-notice")File.Delete(Path.Combine(root,"NOTICE.txt"));
            if(mode=="corrupt-scene")File.WriteAllText(Path.Combine(root,"scene.json"),"{");
            if(mode=="runtime-null")File.WriteAllText(Path.Combine(root,"SceneStarter.runtimeconfig.json"),"""{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":null}}}""");
            if(mode=="hidden-runtime")
            {
                var dep=Path.Combine(root,"SceneStarter.deps.json");
                File.WriteAllText(dep,File.ReadAllText(dep).Replace("SceneStarter.dll","Bed.Editor.dll"));
            }
            using var output=new StringWriter();using var error=new StringWriter();
            Assert.Equal(mode=="valid"?0:3,SceneStarter.Program.Run(["--review",root],output,error));
            if(mode!="valid"){Assert.DoesNotContain("smoke passed",output.ToString());Assert.Contains("refused",error.ToString());return;}
            var files=SceneStarter.ExportInventory.Read(root);
            Assert.DoesNotContain(files.Keys,k=>k.Contains("Editor",StringComparison.Ordinal));
            GameContentNode Node(KeyValuePair<string,byte[]> f)=>new(new(f.Key,f.Key,"runtime"),f.Value,
                Convert.ToHexStringLower(SHA256.HashData(f.Value)),[],true,ExportSource.GameOwned);
            var notices=files.Where(f=>f.Key is "LICENSE.txt" or "NOTICE.txt").Select(Node).ToArray();
            var content=files.Where(f=>f.Key is not ("LICENSE.txt" or "NOTICE.txt")).Select(Node).ToArray();
            var profile=GameBuildProfile.Create("scene-starter","samples/SceneStarter/SceneStarter.csproj",GameBuildTarget.WindowsX64,content.Select(n=>n.Output));
            // Synthetic admission receipt for this test's captured source scene, not owner acceptance of a release.
            var sourceNode=Node(new("scene.json",files["scene.json"]));
            var sourceProfile=GameBuildProfile.Create("scene-starter","samples/SceneStarter/SceneStarter.csproj",GameBuildTarget.WindowsX64,[sourceNode.Output]);
            var source=GameContentManifest.Create(sourceProfile,"scene-starter-review","saved-sample",true,[sourceNode]);
            var origin=ExportOriginReview.Evaluate(source,[]);
            var check=new ExportCheckReceipt(source.Hash,"synthetic-review",ExportCheckVerdict.Passed,DateTimeOffset.UtcNow,TimeSpan.Zero);
            var declaration=new BuildOutputDeclaration(source,["synthetic-review"],[check],origin,profile,
                content.Select(n=>new BuildOutputItem(n.Output,n.Dependencies)),notices.Select(n=>new BuildOutputNotice(n,[])));
            var captured=BuildOutputInventory.Capture(root,declaration,Guid.NewGuid(),new string('a',64));
            Assert.Equal(source.Hash,captured.Manifest.SourceManifestHash);
            var stage=captured.Staging;
            foreach(var f in files)Assert.True(stage.Record(stage.Id,f.Key,f.Value,out var why),why);
            Assert.Equal(ExportStagingState.Ready,stage.State);Assert.Equal(new string('a',64),stage.PriorGoodManifestHash);
            var changed=files["scene.json"].Concat(new byte[]{32}).ToArray();
            Assert.False(stage.Record(stage.Id,"scene.json",changed,out _));Assert.Equal(ExportStagingState.Failed,stage.State);
            Assert.Equal(new string('a',64),stage.PriorGoodManifestHash);
        }
        finally{Directory.Delete(root,true);}
    }
}
