using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bed.Editor;
using Bed.Editor.App;
using Xunit;
namespace Bed.Tests;
public sealed class ExportSellTests
{
    [Fact]
    public void Sample_release_plan_retains_original_graph_and_pins_every_added_file()
    {
        var plan=CapturedExportTests.Plan(CapturedExportTests.Input(),true);
        var files=ExportReleaseFiles.Create(plan);var release=ExportReleaseFiles.AddToPlan(plan,files);
        Assert.Equal(plan.Admission,release.Admission);
        foreach(var path in plan.Outputs.Paths)Assert.Contains(path,release.Outputs.Paths);
        Assert.Contains("CREDITS.txt",release.Outputs.Paths);Assert.Contains("STEAM-AI-DISCLOSURE-DRAFT.txt",release.Outputs.Paths);
        Assert.Contains("LOGO_BRUTAL_WHT.png",release.Outputs.Paths);Assert.Contains("licenses/BRUTAL-LICENSE.txt",release.Outputs.Paths);
        Assert.Contains(ExportReleaseFiles.Credit,Encoding.UTF8.GetString(files["CREDITS.txt"]));
        var draft=Encoding.UTF8.GetString(files["STEAM-AI-DISCLOSURE-DRAFT.txt"]);
        Assert.StartsWith("DRAFT - CHECK",draft);Assert.Contains("Live-generation guardrails",draft);Assert.Contains("history is unknown",draft);
        Assert.Contains("A logo file alone does not satisfy",Encoding.UTF8.GetString(files["BRUTAL-RELEASE-CHECKLIST.txt"]));
        Assert.Equal(new byte[]{137,80,78,71},files["LOGO_BRUTAL_WHT.png"][..4]);
        Assert.Throws<ArgumentException>(()=>ExportReleaseFiles.AddToPlan(release,files));
    }
    [Theory]
    [InlineData(AiUse.Allowed)] [InlineData(AiUse.Forbidden)] [InlineData(AiUse.Unknown)]
    public void Missing_licence_names_the_asset_and_ai_permission_never_becomes_generation_history(AiUse permission)
    {
        var plan=CapturedExportTests.Plan(CapturedExportTests.Input(),true);
        using var settings=JsonDocument.Parse("{}");
        var record=new AssetRecord(new string('a',32),"original-crate.png",new string('b',64),"png",1,settings.RootElement.Clone(),new("Creator","","Created in-house",Declared:AssetOrigin.Ours),permission);
        // Real admission still applies its existing rights gate, with the same captured game-owned fixture inputs.
        var admission=SavedBuildAdmission.Prepare(plan.Admission.SavedInput,plan.Admission.Source.Profile,"fixture",true,_=>plan.Admission.Source.Nodes,
            ["check"],m=>[new(m.Hash,"check",ExportCheckVerdict.Passed,DateTimeOffset.UtcNow,TimeSpan.Zero)],[record]);
        var declared=admission.DeclareOutputs(plan.Outputs.Profile,plan.Outputs.Profile.Content.Select(map=>new BuildOutputItem(map,[])),[]);
        var withRecord=new GameExportPlan(admission,declared);
        var files=ExportReleaseFiles.Create(withRecord);
        var credits=Encoding.UTF8.GetString(files["CREDITS.txt"]);Assert.Contains("original-crate.png",credits);Assert.Contains("Licence: MISSING",credits);
        var draft=Encoding.UTF8.GetString(files["STEAM-AI-DISCLOSURE-DRAFT.txt"]);
        Assert.Contains("generation history: UNKNOWN",draft);Assert.Contains("AI use permission: "+permission,draft);
    }
    [Fact]
    public void Unexpected_child_output_is_refused_before_release_files_are_added()
    {
        using var owned=new CapturedExportTests.Owned();var plan=CapturedExportTests.Plan(CapturedExportTests.Input(),false);
        // A declaration without NOTICE gets the full official file. An existing unrelated file is never overwritten.
        var files=ExportReleaseFiles.Create(plan);
        Assert.Contains(ExportReleaseFiles.Credit,Encoding.UTF8.GetString(files["NOTICE.txt"]));
        Assert.Contains("BRUTAL License v1.0",Encoding.UTF8.GetString(files["NOTICE.txt"]));
        foreach(var node in plan.Admission.Source.Nodes)
        {var bytes=new byte[node.Length];node.CopyTo(bytes);File.WriteAllBytes(Path.Combine(owned.Root,node.Output.Destination),bytes);}
        File.WriteAllText(Path.Combine(owned.Root,"unrelated.txt"),"keep");
        Assert.Throws<IOException>(()=>ExportReleaseFiles.Write(owned.Root,plan,files));
        Assert.False(File.Exists(Path.Combine(owned.Root,"CREDITS.txt")));
        Assert.Equal("keep",File.ReadAllText(Path.Combine(owned.Root,"unrelated.txt")));
    }
    [Fact]
    public async Task Real_sample_export_adds_release_files_before_inventory_and_keeps_them_in_verified_copy()
    {
        using var owned=new CapturedExportTests.Owned();using var logs=new CapturedExportTests.Owned();var plan=CapturedExportTests.Plan(CapturedExportTests.Input(),true);
        var runtime=Path.Combine(AppContext.BaseDirectory,"SceneStarterReview");
        var host=new ExportBuildHost(4096,32,TimeSpan.FromSeconds(10),logs.Root);
        using var operation=host.Start(new(Path.Combine(runtime,"SceneStarter.exe"),runtime,["--build","{input}","{output}"]),owned.Root,false,plan);
        var receipt=await operation.Completion.WaitAsync(TimeSpan.FromSeconds(15));Assert.True(receipt.Succeeded,receipt.Reason);
        var files=ExportReleaseFiles.Create(plan);
        foreach(var file in files)Assert.Equal(file.Value,File.ReadAllBytes(Path.Combine(receipt.OutputDirectory,file.Key)));
        var smoke=new GameLaunchSpec(Path.Combine(receipt.OutputDirectory,"SceneStarter.exe"),receipt.OutputDirectory,["--project",".","--seconds","0"]);
        operation.Dispose();
        var result=await host.Smoke(plan,receipt,operation.OutputManifest!,smoke,CancellationToken.None);
        Assert.NotNull(result.Build);
        var destination=Path.Combine(owned.Root,"copy");Assert.True(result.Build.CopyTo(destination,out var reason),reason);
        foreach(var file in files)Assert.Equal(file.Value,File.ReadAllBytes(Path.Combine(destination,file.Key)));
    }
}
