using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Editor;
namespace Bed.Agent.Cli;

internal static class AgentBuildCommand
{
    internal static JsonObject Run(AgentSession session,string root,Func<PlaySettings?>? defaults,string preferences)
    => AssetAiAccess.Guard(()=>RunAllowed(session,root,defaults,preferences),root,session.Project.DataFolder,session.Project.SchemaFolder,session.AssetFolder);
    private static JsonObject RunAllowed(AgentSession session,string root,Func<PlaySettings?>? defaults,string preferences)
    {
        try
        {
            if(session.Project.ReadOnly)return Refuse("This connection can only read files, so it cannot build the game.");
            if(session.Project.Files.Any(f=>f.Dirty))return Refuse("Save changes before building.");
            var settings=PlaySettingsStore.Load(root,defaults?.Invoke());
            if(settings is null)return Refuse("This data project has no game build. Set a build command in BED's Tools > Play settings.");
            if(!PlaySettingsResolver.TryResolve(root,settings,out var launch,out var why))return Refuse(why!);
            var result=ProjectBuildCommand.RunAsync(launch!,new PlayApprovals(preferences),false).GetAwaiter().GetResult();
            var json=JsonSerializer.SerializeToNode(result,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})!.AsObject();
            if(!result.Success)json["error"]=result.Summary;
            json["next"]=result.Success?"Report the observed build result. Gameplay was not tested.":"Read errors and output. Make the needed change, save, then call build again.";
            return json;
        }
        catch(Exception e)when(e is not OutOfMemoryException)
        {return Refuse(e.Message);}
    }
    private static JsonObject Refuse(string why)=>new(){["error"]=why,["next"]="Use BED's Tools > Build game to inspect and approve the exact build command. This tool never approves commands."};
    internal static string RootFor(string data)=>AssetAiAccess.RootFor(data);
}
