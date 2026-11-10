using System.Security.Cryptography;
using System.Text;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.App;
using Brutal.Authoring.Editor.ImGui;
using Xunit;
namespace Brutal.Authoring.Tests;
public sealed class ExportBuildFacadeTests
{
    private static PlaySnapshot Snapshot(string text="saved",PlayInputChoice choice=PlayInputChoice.Saved)=>
        PlaySnapshot.Create(new("scene",4,text,"draft"),choice,d=>new("fixture",Encoding.UTF8.GetBytes(d.Text))).Snapshot!;
    private static GameExportPlan Plan(PlaySnapshot saved)
    {
        var mapping=new GameContentMapping("source","result.bin","utf8");
        var admission=SavedBuildAdmission.Prepare(saved,GameBuildProfile.Create("source","Fixture.csproj",GameBuildTarget.WindowsX64,[mapping]),"fixture",true,
            s=>{var bytes=s.CopyPayload();return [new(mapping,bytes,Convert.ToHexStringLower(SHA256.HashData(bytes)),[],true,ExportSource.GameOwned)];},
            ["fixture.text"],m=>{var node=Assert.Single(m.Nodes);var bytes=new byte[checked((int)node.Length)];node.CopyTo(bytes);
                return [new(m.Hash,"fixture.text",bytes.SequenceEqual(saved.CopyPayload())?ExportCheckVerdict.Passed:ExportCheckVerdict.Failed,DateTimeOffset.UtcNow,TimeSpan.Zero)];},[]);
        var output=new GameContentMapping("runtime","result.bin","synthetic");
        return new(admission,admission.DeclareOutputs(GameBuildProfile.Create("output","Fixture.csproj",GameBuildTarget.WindowsX64,[output]),[new(output,[])],[]),"result.bin");
    }
    [Fact] public void Receipt_and_logs_are_generic_and_ui_host_does_not_reference_App()
    {
        Assert.Same(typeof(GameLaunchSpec).Assembly,typeof(GameBuildReceipt).Assembly);
        Assert.Same(typeof(GameBuildReceipt).Assembly,typeof(GameBuildLog).Assembly);
        Assert.Null(typeof(GameBuildHost).Assembly.GetType("Brutal.Authoring.Editor.App.GameBuildReceipt"));
        Assert.Null(typeof(GameBuildHost).Assembly.GetType("Brutal.Authoring.Editor.App.GameBuildLog"));
        Assert.DoesNotContain(typeof(IGameBuildHost).Assembly.GetReferencedAssemblies(),a=>a.Name=="Brutal.Authoring.Editor.App");
    }
    [Fact] public void Declaration_uses_only_saved_input_and_refuses_a_plan_for_another_snapshot()
    {
        var saved=Snapshot();var plan=Plan(saved);var calls=0;
        var declaration=new GameExportDeclaration(d=>new("fixture",Encoding.UTF8.GetBytes(d.Text)),s=>{calls++;return plan;},
            (root,_)=>new(Path.Combine(root,"runtime.exe"),root,["--smoke"]));
        Assert.Same(plan,declaration.Prepare(saved));Assert.Equal(1,calls);
        Assert.Throws<InvalidOperationException>(()=>declaration.Prepare(Snapshot(choice:PlayInputChoice.Draft)));Assert.Equal(1,calls);
        Assert.Throws<InvalidOperationException>(()=>declaration.Prepare(Snapshot("another saved value")));Assert.Equal(2,calls);
        Assert.Throws<ArgumentException>(()=>new GameExportPlan(Plan(Snapshot("different")).Admission,plan.Outputs));
        Assert.Throws<ArgumentException>(()=>declaration.Smoke("relative",plan.Admission.Source));
        var spec=declaration.Smoke(@"C:\owned\output",plan.Admission.Source);Assert.Equal(@"C:\owned\output\runtime.exe",spec.Executable);Assert.Equal(new[]{"--smoke"},spec.Arguments);
    }
    [Fact] public void Dirty_input_refuses_before_any_output_directory_or_process_is_created()
    {
        var root=Path.Combine(Path.GetTempPath(),"bed-export-dirty-"+Guid.NewGuid().ToString("N"));
        IGameBuildHost host=new ExportBuildHost(1024,16,TimeSpan.FromSeconds(5));
        Assert.Throws<InvalidOperationException>(()=>host.Start(new(@"C:\invented\builder.exe",@"C:\invented",["{output}"]),root,true,Plan(Snapshot())));
        Assert.False(Path.Exists(root));
    }
    [Fact] public async Task Facade_forwards_actual_pinned_completion_logs_and_idempotent_disposal_without_claiming_smoke()
    {
        if(!OperatingSystem.IsWindows())return;
        var root=Path.Combine(Path.GetTempPath(),"bed-export-facade-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var plan=Plan(Snapshot());var helper=Path.Combine(AppContext.BaseDirectory,"LinkedBuildTestGame","LinkedBuildTestGame.exe");
            IGameBuildHost host=new ExportBuildHost(1024,16,TimeSpan.FromSeconds(5));
            using var operation=host.Start(new(helper,root,["captured","{output}","{input}"]),root,false,plan);
            var receipt=await operation.Completion.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.True(receipt.Succeeded,receipt.Reason);Assert.Equal(0,receipt.ExitCode);Assert.Equal(plan.Admission.SnapshotHash,receipt.SavedRevision);
            var manifest=Assert.IsType<GameContentManifest>(operation.OutputManifest);Assert.Equal(plan.Admission.Source.Hash,manifest.SourceManifestHash);Assert.Equal(manifest.Hash,receipt.ManifestHash);
            Assert.Equal(plan.Admission.SavedInput.CopyPayload(),File.ReadAllBytes(Path.Combine(receipt.OutputDirectory,"result.bin")));
            Assert.Equal("result.bin",Assert.Single(receipt.Outputs).Path);
            Assert.Contains(operation.DrainLogs(),line=>line.Error&&line.Text=="Synthetic diagnostic");Assert.Empty(operation.DrainLogs());
            Assert.Equal("Build outputs verified; not published.",receipt.Reason);
            operation.Dispose();operation.Dispose();Assert.True(File.Exists(Path.Combine(receipt.OutputDirectory,"result.bin")));
        }
        finally
        {
            var full=Path.GetFullPath(root);if(!full.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("bed-export-facade-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected fixture path.");
            Directory.Delete(full,true);
        }
    }
}
