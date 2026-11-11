using System.Security.Cryptography;
using Bed.Editor;
using Bed.Editor.App;
using Xunit;
namespace Bed.Tests;
public sealed class VerifiedExportCopyTests
{
    private static async Task<(GameExportPlan Plan,GameBuildReceipt Receipt,GameContentManifest Manifest)> Build(string root)
    {
        var plan=CapturedExportTests.Plan(CapturedExportTests.Input(true),true);var runtime=Path.Combine(AppContext.BaseDirectory,"SceneStarterReview");
        using var operation=new ExportBuildHost(4096,32,TimeSpan.FromSeconds(8)).Start(new(Path.Combine(runtime,"SceneStarter.exe"),runtime,["--build","{input}","{output}"]),root,false,plan);
        var receipt=await operation.Completion.WaitAsync(TimeSpan.FromSeconds(12));Assert.True(receipt.Succeeded,receipt.Reason);return(plan,receipt,operation.OutputManifest!);
    }
    private static GameLaunchSpec Smoke(GameBuildReceipt receipt,string action="--project")=>new(Path.Combine(receipt.OutputDirectory,"SceneStarter.exe"),receipt.OutputDirectory,action=="--project"?["--project",".","--seconds","0"]:[action,Path.Combine(receipt.OutputDirectory,"data","level.json")]);
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Actual_smoke_issues_copy_capability_only_for_new_or_empty_destination(bool existingEmpty)
    {
        using var owned=new CapturedExportTests.Owned();var build=await Build(owned.Root);
        var verified=await VerifiedGameBuild.SmokeAsync(build.Plan,build.Receipt,build.Manifest,Smoke(build.Receipt),TimeSpan.FromSeconds(8));
        Assert.NotNull(verified.Build);Assert.Contains(verified.Logs,line=>line.Text.Contains("loaded saved level",StringComparison.Ordinal));
        var target=Path.Combine(owned.Root,"copied");if(existingEmpty)Directory.CreateDirectory(target);
        Assert.True(verified.Build.CopyTo(target,out var why),why);
        Assert.Equal(build.Receipt.Outputs.Select(o=>o.Path).Order(),Directory.GetFiles(target,"*",SearchOption.AllDirectories).Select(path=>Path.GetRelativePath(target,path).Replace('\\','/')).Order());
        foreach(var artifact in build.Receipt.Outputs)
        {
            var bytes=File.ReadAllBytes(Path.Combine(target,artifact.Path));Assert.Equal(artifact.Length,bytes.Length);Assert.Equal(artifact.Sha256,Convert.ToHexStringLower(SHA256.HashData(bytes)));
            Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(build.Receipt.OutputDirectory,artifact.Path)));
        }
        var sentinel=Path.Combine(target,"keep.txt");File.WriteAllText(sentinel,"existing user content");
        Assert.False(verified.Build.CopyTo(target,out why));Assert.Contains("new or empty",why);Assert.Equal("existing user content",File.ReadAllText(sentinel));
        Assert.False(verified.Build.CopyTo(Path.Combine(build.Receipt.OutputDirectory,"nested"),out why));Assert.Contains("outside",why);Assert.False(Directory.Exists(Path.Combine(build.Receipt.OutputDirectory,"nested")));
    }
    [Fact] public async Task Readme_is_manifest_pinned_copied_and_tampering_refuses_copy()
    {
        using var owned=new CapturedExportTests.Owned();var build=await Build(owned.Root);
        var readme=Assert.Single(build.Manifest.Nodes,n=>n.Output.Destination=="README.txt");
        var path=Path.Combine(build.Receipt.OutputDirectory,"README.txt");var bytes=File.ReadAllBytes(path);
        Assert.Equal(readme.Sha256,Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var text=System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains(".NET 10 runtime for Windows x64 (Microsoft.NETCore.App)",text);
        Assert.Contains(@".\SceneStarter.exe --project . --seconds 0",text);
        Assert.Contains("saved level and prepared models and textures",text);
        var verified=await VerifiedGameBuild.SmokeAsync(build.Plan,build.Receipt,build.Manifest,Smoke(build.Receipt),TimeSpan.FromSeconds(8));Assert.NotNull(verified.Build);
        var target=Path.Combine(owned.Root,"copied");Assert.True(verified.Build.CopyTo(target,out var why),why);
        Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(target,"README.txt")));
        File.AppendAllText(path,"Changed run command");var refused=Path.Combine(owned.Root,"refused");
        Assert.False(verified.Build.CopyTo(refused,out why));Assert.False(Directory.Exists(refused));
    }
    [Fact] public async Task Changed_output_before_smoke_never_issues_copy_capability()
    {
        using var owned=new CapturedExportTests.Owned();var build=await Build(owned.Root);File.AppendAllText(Path.Combine(build.Receipt.OutputDirectory,"data","level.json")," ");
        var verified=await VerifiedGameBuild.SmokeAsync(build.Plan,build.Receipt,build.Manifest,Smoke(build.Receipt),TimeSpan.FromSeconds(8));
        Assert.Null(verified.Build);Assert.Empty(verified.Logs);Assert.Contains("doesn't match the saved files",verified.Reason);
    }
    [Fact] public async Task Changed_output_after_successful_smoke_refuses_copy_before_destination_creation()
    {
        using var owned=new CapturedExportTests.Owned();var build=await Build(owned.Root);
        var verified=await VerifiedGameBuild.SmokeAsync(build.Plan,build.Receipt,build.Manifest,Smoke(build.Receipt),TimeSpan.FromSeconds(8));Assert.NotNull(verified.Build);
        File.AppendAllText(Path.Combine(build.Receipt.OutputDirectory,"data","level.json")," ");var target=Path.Combine(owned.Root,"must-not-exist");
        Assert.False(verified.Build.CopyTo(target,out var why));Assert.Contains("doesn't match the saved files",why);Assert.False(Path.Exists(target));
    }
    [Fact] public async Task Actual_nonzero_smoke_and_pre_canceled_smoke_do_not_enable_copy()
    {
        using var owned=new CapturedExportTests.Owned();var build=await Build(owned.Root);
        // The retained strict legacy fixture smoke must refuse this edited asset-bearing scene; no assertion weakened.
        var failed=await VerifiedGameBuild.SmokeAsync(build.Plan,build.Receipt,build.Manifest,Smoke(build.Receipt,"--smoke"),TimeSpan.FromSeconds(8));
        Assert.Null(failed.Build);Assert.Contains("code 3",failed.Reason);Assert.Contains(failed.Logs,line=>line.Error&&line.Text.Contains("refused",StringComparison.Ordinal));
        var canceled=await VerifiedGameBuild.SmokeAsync(build.Plan,build.Receipt,build.Manifest,Smoke(build.Receipt),TimeSpan.FromSeconds(8),cancellation:new CancellationToken(true));
        Assert.Null(canceled.Build);Assert.Contains("canceled",canceled.Reason);Assert.Empty(canceled.Logs);
    }
}
