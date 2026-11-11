using System.Text;
using System.Text.Json;
using Bed.Runtime;
namespace Bed.Editor.ImGui;

internal sealed record PlayDiagnostic(string File,string Pointer,string Message)
{
    internal static PlayDiagnostic? Read(string line)
    {
        const string prefix="BED_DIAGNOSTIC_V1 ";
        if(!line.StartsWith(prefix,StringComparison.Ordinal)||line.Length>4096||line.Contains('\r')||line.Contains('\n'))return null;
        try
        {
            if(new UTF8Encoding(false,true).GetByteCount(line)>4096)return null;
            using var json=JsonDocument.Parse(line[prefix.Length..],new(){MaxDepth=4});
            if(json.RootElement.ValueKind!=JsonValueKind.Object)return null;
            var fields=json.RootElement.EnumerateObject().ToArray();
            if(fields.Length!=3||fields.Any(p=>p.Value.ValueKind!=JsonValueKind.String)||
                !fields.Select(p=>p.Name).Order(StringComparer.Ordinal).SequenceEqual(new[]{"file","message","pointer"}))return null;
            var result=new PlayDiagnostic(json.RootElement.GetProperty("file"u8).GetString()!,json.RootElement.GetProperty("pointer"u8).GetString()!,json.RootElement.GetProperty("message"u8).GetString()!);
            _=ProjectDiagnostic.Format(result.File,result.Pointer,result.Message);
            return result;
        }
        catch(Exception error)when(error is JsonException or ArgumentException or InvalidOperationException){return null;}
    }
}
