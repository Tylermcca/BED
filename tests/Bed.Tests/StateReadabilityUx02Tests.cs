using System.Collections.Immutable;
using System.Text.Json;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.App;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class StateReadabilityUx02Tests
{
    private sealed class Build:IGameBuildOperation
    {
        internal readonly TaskCompletionSource<GameBuildReceipt> Done=new();public Task<GameBuildReceipt> Completion=>Done.Task;
        public GameContentManifest? OutputManifest{get;set;}public ImmutableArray<GameBuildLog> DrainLogs()=>[];public void Cancel(){}public void Dispose(){}
    }
    private sealed class Verified(string root):IVerifiedGameBuild
    {public string OutputDirectory=>root;public bool CopyTo(string folder,out string reason,CancellationToken cancellation=default)=>throw new InvalidOperationException("No copy in a readability audit.");public void Dispose(){}}
    private sealed class Exports(string root):IGameExportHost
    {
        internal readonly Build Build=new();internal int Starts;
        public IGameBuildOperation Start(GameBuildSpec spec,string project,bool dirty,GameExportPlan plan){Assert.False(dirty);Starts++;Build.OutputManifest=plan.Admission.Source;return Build;}
        public Task<GameExportSmokeResult> Smoke(GameExportPlan plan,GameBuildReceipt receipt,GameContentManifest outputs,GameLaunchSpec smoke,CancellationToken token)=>Task.FromResult(new GameExportSmokeResult(new Verified(root),"Verified fixture; no process started."));
    }
    public static IEnumerable<object[]> Matrix(){foreach(var width in new[]{1280,1920})foreach(var scale in new[]{1f,1.5f,2f})foreach(var theme in new[]{EditorThemePreference.Light,EditorThemePreference.Dark})foreach(var mode in new[]{"assets-ready","assets-missing","console-errors","export-running","export-error","export-verified"})yield return [width,width==1280?720:1080,scale,theme,mode];}
    [Theory][MemberData(nameof(Matrix))]
    public void Populated_assets_console_and_export_states_are_readable(int width,int height,float scale,EditorThemePreference theme,string mode)
    {
        var root=Directory.CreateTempSubdirectory("readability-state-").FullName;var previous=EditorTheme.Preference;
        try
        {
            using var host=new HeadlessHost(width,height){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());new DpiScale().Apply(scale);EditorTheme.Preference=theme;EditorTheme.Refresh();
            Action draw;ExportBuildDialog? export=null;
            if(mode.StartsWith("assets",StringComparison.Ordinal))
            {
                var path=Path.Combine(root,"model.txt");File.WriteAllText(path,"Original metadata fixture.");
                var record=new AssetRecord("11111111111111111111111111111111","model.txt",AssetRecord.HashOf(path),"fixture",1,JsonSerializer.SerializeToElement(new{}),new("fixture","self-authored","fixture",Declared:AssetOrigin.Ours),AiUse.Forbidden);
                File.WriteAllText(path+AssetRecord.Suffix,record.Write());if(mode=="assets-missing")File.Delete(path);
                var panel=new AssetsPanel(new AssetBrowser(root,".txt"));
                void Draw(){ImGui.SetNextWindowPos(new(0,0));ImGui.SetNextWindowSize(new(width,height));if(Ui.Begin("Asset details"u8))panel.Draw();Ui.End();}
                draw=Draw;host.Frames(draw,4);var item=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.ListItem&&n.Label.StartsWith("model.txt",StringComparison.Ordinal));
                var request=Ui.RequestAction(item.Id,UiActions.Select);host.Frames(draw,4);Assert.Equal(UiActionOutcome.Applied,request.Outcome);Assert.NotNull(panel.Selected);
            }
            else if(mode=="console-errors")
            {
                var correlation=new PlayCorrelation(Guid.NewGuid(),0,new string('0',64));var buffer=new PlayLogBuffer(correlation,20,500,5000,["logic.json"]);
                for(var i=0;i<12;i++)Assert.True(buffer.Add(correlation,PlayLogSeverity.Error,"Fixture failure "+i+": fix the selected object, save and press Play again.",new("logic.json","crate",i+1)));
                var panel=new ConsolePanel(buffer);
                draw=()=>{ImGui.SetNextWindowPos(new(0,0));ImGui.SetNextWindowSize(new(width,height));if(Ui.Begin("Console state"u8))panel.Draw();Ui.End();};
            }
            else
            {
                var saved=CapturedExportTests.Input();var plan=CapturedExportTests.Plan(saved,false);var builds=new Exports(root);
                var declaration=new GameExportDeclaration(_=>new(saved.Format,saved.CopyPayload()),_=>plan,(folder,_)=>new(Path.Combine(folder,"sample.exe"),folder,[]));
                export=new(builds,new(Path.Combine(root,"builder.exe"),root,["{input}","{output}"]),declaration,root,()=>saved,()=>false){Open=true};
                Assert.True(export.Start());Assert.Equal(1,builds.Starts);
                if(mode!="export-running")builds.Build.Done.SetResult(new("fixture","fixture",root,mode=="export-error"?1:0,mode!="export-error",mode=="export-error"?"The fixture builder failed. Fix its settings and build the saved files again.":"Built fixture",[]));
                draw=export.Draw;host.Frames(draw,5);if(mode=="export-verified")Assert.True(export.CanCopy);
            }
            try
            {
                host.Frames(draw,5);host.Drawing=true;host.Frame(draw);
                var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent;
                var folder=Path.Combine(repo!.FullName,".validation","UX02");Directory.CreateDirectory(folder);var name=$"{mode}-{width}-{scale:0.0}-{theme}";
                host.Save(Path.Combine(folder,name+".png"));ReadabilityUx02Tests.AssertReadable(Ui.Snapshot(),scale,Path.Combine(folder,name+".txt"));
            }
            finally{export?.Dispose();}
        }
        finally{EditorTheme.Preference=previous;Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
}
