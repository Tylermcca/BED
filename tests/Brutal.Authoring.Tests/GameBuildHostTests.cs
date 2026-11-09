using System.Security.Cryptography;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class GameBuildHostTests
{
    private static ExportStaging Staging()
    {
        var map=new GameContentMapping("output","result.bin","synthetic");
        var profile=GameBuildProfile.Create("test","Synthetic.csproj",GameBuildTarget.WindowsX64,[map]);
        var manifest=GameContentManifest.Create(profile,"synthetic","saved-1",true,[new(map,new byte[]{1},Convert.ToHexStringLower(SHA256.HashData(new byte[]{1})),[],true,ExportSource.GameOwned)]);
        return ExportStaging.Create(Guid.NewGuid(),manifest,new string('a',64),[],true);
    }
    private static GameBuildSpec Spec()=>new(@"C:\invented\builder.exe",@"C:\invented",["--output","{output}"]);
    [Fact] public void Spec_copies_argv_and_requires_one_whole_placeholder()
    {
        string[] args=["--output","{output}"];var spec=new GameBuildSpec(@"C:\invented\builder.exe",@"C:\invented",args);args[1]="changed";
        Assert.Equal("{output}",spec.Arguments[1]);
        Assert.Throws<ArgumentException>(()=>new GameBuildSpec(spec.Executable,spec.WorkingDirectory,[]));
        Assert.Throws<ArgumentException>(()=>new GameBuildSpec(spec.Executable,spec.WorkingDirectory,["{output}","{output}"]));
        Assert.Throws<ArgumentException>(()=>new GameBuildSpec(spec.Executable,spec.WorkingDirectory,["--out={output}"]));
    }
    [Fact] public void Unsaved_or_invalid_revision_and_budget_refuse_before_resources()
    {
        var staging=Staging();
        Assert.Throws<InvalidOperationException>(()=>new GameBuildHost(Spec(),@"C:\invented","saved-1",true,staging,1024,16,TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentException>(()=>new GameBuildHost(Spec(),@"C:\invented","",false,staging,1024,16,TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new GameBuildHost(Spec(),@"C:\invented","saved-1",false,staging,1024,0,TimeSpan.FromSeconds(5)));
        Assert.Equal(ExportStagingState.Collecting,staging.State);
    }
    [Fact] public void Cancel_before_start_is_terminal_and_preserves_prior_good()
    {
        var staging=Staging();using var host=new GameBuildHost(Spec(),@"C:\invented","saved-1",false,staging,1024,16,TimeSpan.FromSeconds(5));
        host.Cancel();host.Cancel();Assert.Null(host.Completion);Assert.Empty(host.DrainLogs());
        Assert.Throws<InvalidOperationException>(host.Start);Assert.Equal(new string('a',64),staging.PriorGoodManifestHash);
        Assert.Equal(ExportStagingState.Canceled,staging.State);
    }
    private sealed class Owned : IDisposable
    {
        public readonly string Root=Path.Combine(Path.GetTempPath(),"bed-build-test-"+Guid.NewGuid().ToString("N"));
        public readonly ExportStaging Staged=Staging();
        public GameBuildHost Host {get;}
        public Owned(string mode,int lines=16,int characters=1024,TimeSpan? timeout=null)
        {
            Directory.CreateDirectory(Root);
            var helper=Path.Combine(AppContext.BaseDirectory,"LinkedBuildTestGame","LinkedBuildTestGame.exe");
            Host=new(new(helper,Root,[mode,"{output}"]),Root,"saved-1",false,Staged,characters,lines,timeout??TimeSpan.FromSeconds(5));
        }
        public void Dispose()
        {
            Host.Dispose();
            var full=Path.GetFullPath(Root);var temp=Path.GetFullPath(Path.GetTempPath());
            if(!full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("bed-build-test-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected fixture path.");
            Directory.Delete(full,true);
        }
    }
    [Fact] public async Task Successful_synthetic_build_has_saved_receipt_and_no_publication()
    {
        if(!OperatingSystem.IsWindows())return;
        using var owned=new Owned("ok");owned.Host.Start();var receipt=await owned.Host.Completion!.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.True(receipt.Succeeded,receipt.Reason);Assert.Equal(0,receipt.ExitCode);Assert.Equal("saved-1",receipt.SavedRevision);
        Assert.Equal(owned.Staged.ManifestHash,receipt.ManifestHash);Assert.Single(receipt.Outputs);
        Assert.Equal(Path.Combine(owned.Root,".bed","cache","export",owned.Staged.Id.ToString("N")),receipt.OutputDirectory);
        Assert.Equal(new byte[]{1},File.ReadAllBytes(Path.Combine(receipt.OutputDirectory,"result.bin")));
        Assert.Equal(new string('a',64),owned.Staged.PriorGoodManifestHash);Assert.Equal(ExportStagingState.Ready,owned.Staged.State);
        Assert.Contains(owned.Host.DrainLogs(),line=>line.Error&&line.Text=="Synthetic diagnostic");
        owned.Host.Dispose();Assert.Equal(ExportStagingState.Ready,owned.Staged.State);
    }
    [Theory][InlineData("ok")][InlineData("extra")]
    public async Task Declared_outputs_pin_actual_helper_completion_to_admitted_source_without_replacing_prior_good(string mode)
    {
        if(!OperatingSystem.IsWindows())return;
        // Explicit self-authored source and an actually evaluated fixture check, not a production admission receipt.
        byte[] input=[41];
        var sourceMapping=new GameContentMapping("source","input.bin","synthetic");
        var source=GameContentManifest.Create(GameBuildProfile.Create("fixture","Fixture.csproj",GameBuildTarget.WindowsX64,[sourceMapping]),
            "synthetic","saved-1",true,[new(sourceMapping,input,Convert.ToHexStringLower(SHA256.HashData(input)),[],true,ExportSource.GameOwned)]);
        var check=new ExportCheckReceipt(source.Hash,"fixture.input",input.SequenceEqual(new byte[]{41})?ExportCheckVerdict.Passed:ExportCheckVerdict.Failed,
            DateTimeOffset.UtcNow,TimeSpan.Zero);
        var origin=ExportOriginReview.Evaluate(source,[]);
        Assert.True(origin.Accepted);
        var output=new GameContentMapping("output","result.bin","synthetic");
        var declaration=new BuildOutputDeclaration(source,["fixture.input"],[check],origin,
            GameBuildProfile.Create("fixture","Fixture.csproj",GameBuildTarget.WindowsX64,[output]),[new(output,[])],[]);
        var root=Path.Combine(Path.GetTempPath(),"bed-declared-build-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] priorBytes=[7,8,9];
            var priorHash=Convert.ToHexStringLower(SHA256.HashData(priorBytes));
            var priorDirectory=Path.Combine(root,"prior-good");Directory.CreateDirectory(priorDirectory);
            var priorFile=Path.Combine(priorDirectory,"result.bin");File.WriteAllBytes(priorFile,priorBytes);
            var helper=Path.Combine(AppContext.BaseDirectory,"LinkedBuildTestGame","LinkedBuildTestGame.exe");
            using var host=new GameBuildHost(new(helper,root,[mode,"{output}"]),root,"saved-1",false,declaration,1024,16,TimeSpan.FromSeconds(5),priorHash);
            Assert.Null(host.OutputManifest);Assert.Null(host.Completion);
            host.Start();var receipt=await host.Completion!.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal(0,receipt.ExitCode);Assert.Equal("saved-1",receipt.SavedRevision);
            Assert.Equal(Path.Combine(root,".bed","cache","export"),Path.GetDirectoryName(receipt.OutputDirectory));
            Assert.True(Guid.TryParseExact(Path.GetFileName(receipt.OutputDirectory),"N",out _));
            if(mode=="ok")
            {
                Assert.True(receipt.Succeeded,receipt.Reason);
                var manifest=Assert.IsType<GameContentManifest>(host.OutputManifest);
                Assert.Equal(source.Hash,manifest.SourceManifestHash);Assert.Equal(manifest.Hash,receipt.ManifestHash);
                Assert.Equal("result.bin",Assert.Single(receipt.Outputs).Path);
                var node=Assert.Single(manifest.Nodes);byte[] pinned=new byte[checked((int)node.Length)];node.CopyTo(pinned);
                Assert.Equal(new byte[]{1},pinned);Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(pinned)),node.Sha256);
                Assert.Equal(source.Hash,node.Source!.SourceHash);
                Assert.Equal(pinned,File.ReadAllBytes(Path.Combine(receipt.OutputDirectory,"result.bin")));
            }
            else
            {
                Assert.False(receipt.Succeeded);Assert.Contains("not-allowed.txt",receipt.Reason);
                Assert.Null(host.OutputManifest);Assert.Empty(receipt.ManifestHash);Assert.Empty(receipt.Outputs);
            }
            host.Dispose();
            Assert.Equal(new[]{priorFile},Directory.GetFiles(priorDirectory));
            Assert.Equal(priorBytes,File.ReadAllBytes(priorFile));
            Assert.Equal(priorHash,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(priorFile))));
        }
        finally
        {
            var full=Path.GetFullPath(root);
            if(!full.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("bed-declared-build-",StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected fixture path.");
            Directory.Delete(full,true);
        }
    }
    [Theory][InlineData("nonzero")][InlineData("wrong")][InlineData("extra")][InlineData("missing")]
    public async Task Failed_or_unexpected_output_never_becomes_success(string mode)
    {
        if(!OperatingSystem.IsWindows())return;
        using var owned=new Owned(mode);owned.Host.Start();var receipt=await owned.Host.Completion!.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.False(receipt.Succeeded);Assert.NotEmpty(receipt.Reason);Assert.Equal(ExportStagingState.Failed,owned.Staged.State);
        Assert.Equal(new string('a',64),owned.Staged.PriorGoodManifestHash);Assert.Empty(receipt.Outputs);
    }
    [Theory][InlineData("long-line",16)][InlineData("burst",2)]
    public async Task Output_budget_refuses_without_unbounded_log_storage(string mode,int lines)
    {
        if(!OperatingSystem.IsWindows())return;
        using var owned=new Owned(mode,lines);owned.Host.Start();var receipt=await owned.Host.Completion!.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.False(receipt.Succeeded);Assert.True(owned.Host.DrainLogs().Length<=lines);
    }
    [Fact] public async Task Cancel_and_deadline_terminate_only_synthetic_job()
    {
        if(!OperatingSystem.IsWindows())return;
        using(var owned=new Owned("hang"))
        {
            owned.Host.Start();owned.Host.Cancel();owned.Host.Cancel();var receipt=await owned.Host.Completion!.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(receipt.Succeeded);Assert.Equal(ExportStagingState.Canceled,owned.Staged.State);
        }
        using(var owned=new Owned("hang",timeout:TimeSpan.FromMilliseconds(150)))
        {
            owned.Host.Start();var receipt=await owned.Host.Completion!.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(receipt.Succeeded);Assert.Contains("time limit",receipt.Reason);
        }
    }
    [Fact] public async Task Existing_output_admission_is_not_reused_or_deleted()
    {
        if(!OperatingSystem.IsWindows())return;
        using var owned=new Owned("ok");var output=Path.Combine(owned.Root,".bed","cache","export",owned.Staged.Id.ToString("N"));
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"keep.txt"),"keep");
        owned.Host.Start();var receipt=await owned.Host.Completion!.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.False(receipt.Succeeded);Assert.Null(receipt.ExitCode);Assert.Equal("keep",File.ReadAllText(Path.Combine(output,"keep.txt")));
    }
}
