using Bed.Editor;
using Bed.Editor.ImGui;
using Xunit;
namespace Bed.Tests;

[Collection(ImGuiCollection.Name)]
public sealed class AssetAiObservationTests
{
    [Theory]
    [InlineData("shot",AiUse.Forbidden)][InlineData("hold",AiUse.Forbidden)][InlineData("live",AiUse.Forbidden)][InlineData("tune",AiUse.Forbidden)][InlineData("stop",AiUse.Forbidden)]
    [InlineData("shot",AiUse.Unknown)][InlineData("hold",AiUse.Unknown)][InlineData("live",AiUse.Unknown)][InlineData("tune",AiUse.Unknown)][InlineData("stop",AiUse.Unknown)]
    public void Active_owned_session_rechecks_permissions_before_picture_input_live_tune_and_stop_replies(string operation,AiUse permission)
    {
        using var copy=new SpriteSampleTests.Copy();copy.OpenAndCompile();
        var target=Assert.Single(Assert.Single(Adapters.Load([Path.Combine(AppContext.BaseDirectory,"adapters/SceneStarterAdapter.dll")]).Loaded).LiveValues(ProjectPaths.In(copy.Root)!));
        var launch=new PlayLaunch(new(Path.Combine(copy.Root,"game","SceneStarter.exe"),copy.Root,["--project","."]),null,copy.Root,[]);
        var live=operation is "live" or "tune";
        using var observation=new PlayObservation();observation.Start(launch,new(copy.Settings),false,live?target.ObjectId:null);
        if(live)Assert.Equal(target.Values.Count,observation.ReadValues(target).Numbers.Count);
        else Assert.NotEmpty(observation.Request("shot")["imageData"]!.GetValue<string>());
        var record=Path.Combine(copy.Root,"assets","block.png.asset.json");
        var permissionWord=permission==AiUse.Forbidden?"forbidden":"unknown";
        var text=File.ReadAllText(record);var changed=text.Replace("\"aiUse\": \"allowed\"","\"aiUse\": \""+permissionWord+"\"");
        Assert.NotEqual(text,changed);File.WriteAllText(record,changed);
        var error=Assert.Throws<InvalidOperationException>(()=>
        {
            switch(operation)
            {
                case "shot":observation.Request("shot");break;
                case "hold":observation.Request("hold","right",.1);break;
                case "stop":observation.Request("stop");break;
                case "live":observation.ReadValues(target);break;
                default:observation.ReadValues(target,field:"jumpStrength",value:16);break;
            }
        });
        Assert.Contains(permissionWord,error.Message);Assert.DoesNotContain("imageData",error.Message);
    }
}
