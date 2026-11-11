using System.Text;
using System.Text.RegularExpressions;
namespace Bed.Editor.ImGui;

// No project content, game stdout or remote service is read here.
internal sealed record ProblemReportContext(string Kind="No project",string? Root=null,string? Preferences=null,IFileDialogs? Dialogs=null);
internal static class ProblemReport
{
    internal const string DefaultRepository="https://github.com/Tylermcca/BED";
    internal const int MaxUrlLength=7500;
    internal static string Redact(string value,string? root=null,string? user=null)
    {
        value=value.Replace('\0',' ');
        // Quoted paths may contain spaces. Unquoted paths are removed to the line/markup delimiter,
        // deliberately preferring excess redaction over retaining part of a private folder name.
        value=Regex.Replace(value,"[\"'](?:[A-Za-z]:[\\\\/]|\\\\\\\\|/)[^\"'\\r\\n]*[\"']","[path]");
        value=Regex.Replace(value,@"(?<![A-Za-z0-9])(?:[A-Za-z]:[\\/]|\\\\)[^\r\n<>|`]*","[path]");
        value=Regex.Replace(value,@"(?<![:/A-Za-z0-9])/(?!/)[^\r\n<>|`]*","[path]");
        foreach(var secret in new[]{root,user??Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}.Where(x=>!string.IsNullOrWhiteSpace(x)).OrderByDescending(x=>x!.Length))
        {
            value=value.Replace(secret!,"[redacted]",StringComparison.OrdinalIgnoreCase);
            if(secret==root)
            {
                var name=Path.GetFileName(secret!.TrimEnd('/','\\'));
                if(name.Length>0)value=Regex.Replace(value,@"(?<![\p{L}\p{N}_])"+Regex.Escape(name)+@"(?![\p{L}\p{N}_])","[project]",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            }
        }
        return value;
    }
    internal static string Format(ProblemReportContext context,string happened="",string expected="",IEnumerable<string>? log=null,bool version=true,bool windows=true,bool kind=true,bool logs=true)
    {
        var b=new StringBuilder("# BED problem report\n\n## What happened\n").Append(happened).Append("\n\n## What you expected\n").Append(expected).Append("\n\n## Details\n");
        if(version)b.Append("BED: ").Append(BuildVersion.Current).Append('\n');
        if(windows)b.Append("Operating system: ").Append(OperatingSystem.IsWindows()?"Windows "+Environment.OSVersion.Version:Environment.OSVersion.Platform.ToString()).Append('\n');
        if(kind)b.Append("Project kind: ").Append(context.Kind).Append('\n');
        if(logs)b.Append("\n## BED log (last 200 lines)\n```text\n").AppendJoin('\n',(log??[]).TakeLast(200).Select(x=>x.Length>2000?x[..2000]:x)).Append("\n```\n");
        return Redact(b.ToString(),context.Root);
    }
    internal static string IssueUrl(string repository,string report)
    {
        if(!Uri.TryCreate(repository,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment))throw new ArgumentException("Choose an HTTPS repository address without a query or fragment.");
        var prefix=repository.TrimEnd('/')+"/issues/new?title="+Uri.EscapeDataString("BED problem report")+"&body=";
        if(prefix.Length>MaxUrlLength-100)throw new ArgumentException("The repository address is too long.");
        var body=report;const string tail="\n\n[Report shortened for browser URL. Paste the complete copied report into the issue.]";
        if(prefix.Length+Uri.EscapeDataString(body).Length>MaxUrlLength)
        {
            var low=0;var high=body.Length;
            while(low<high){var mid=(low+high+1)/2;if(prefix.Length+Uri.EscapeDataString(body[..mid]+tail).Length<=MaxUrlLength)low=mid;else high=mid-1;}
            if(low>0&&char.IsHighSurrogate(body[low-1]))low--;
            body=body[..low]+tail;
        }
        return prefix+Uri.EscapeDataString(body);
    }
}
internal static class ProblemReportHost
{
    // Installed only by the running native app; tests inject a non-window implementation.
    internal static Func<ProblemReportContext,byte[]?>? Capture;
}
