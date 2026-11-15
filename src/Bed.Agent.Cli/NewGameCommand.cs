using System.Text.Json;
using System.Text.Json.Serialization;
using Bed.Editor.ImGui;

namespace Bed.Agent.Cli;

internal static class NewGameCommand
{
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,Converters={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)}};
    internal static int Run(string[] args,TextWriter output,TextWriter errors)
    {
        if(args.Length is <2 or >3||args.Length==3&&args[2]!="--preview")
        {errors.WriteLine("usage: bed-cli new <full-folder> [--preview]");return 2;}
        try
        {
            if(!Path.IsPathFullyQualified(args[1]))throw new ArgumentException("Choose a full path for your new game folder.");
            var destination=Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[1]));
            var location=Path.GetDirectoryName(destination)??throw new ArgumentException("Choose a named folder, not a drive root.");var name=Path.GetFileName(destination);
            var creator=new ProjectCreator();
            if(args.Length==3){var preview=creator.Preview("game",location,name);output.WriteLine(JsonSerializer.Serialize(preview,Json));return preview.CanCreate?0:1;}
            var result=creator.Create("game",location,name);output.WriteLine(JsonSerializer.Serialize(result,Json));return result.Status==ProjectCreationStatus.Created?0:1;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {errors.WriteLine("Your game couldn't be created: "+e.Message);return 2;}
    }
}
