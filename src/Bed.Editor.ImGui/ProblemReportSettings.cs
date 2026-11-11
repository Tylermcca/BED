using System.Text.Json;
namespace Bed.Editor.ImGui;
internal static class ProblemReportSettings
{
    internal static (string Repository,string Note) Read(string? preferences)
    {
        const string fallback="The report repository setting could not be read; using the public BED repository.";
        try
        {
            var file=Path.Combine(preferences??ProblemReportLog.DefaultFolder,"settings.json");
            if(!File.Exists(file))return(ProblemReport.DefaultRepository,"");
            if(new FileInfo(file).Length>65536)return(ProblemReport.DefaultRepository,fallback);
            using var json=JsonDocument.Parse(File.ReadAllText(file));
            if(!json.RootElement.TryGetProperty("reportRepositoryUrl"u8,out var value))return(ProblemReport.DefaultRepository,"");
            if(value.ValueKind!=JsonValueKind.String)return(ProblemReport.DefaultRepository,fallback);
            var url=value.GetString()!;ProblemReport.IssueUrl(url,"");return(url,"");
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException){return(ProblemReport.DefaultRepository,fallback);}
    }
}
