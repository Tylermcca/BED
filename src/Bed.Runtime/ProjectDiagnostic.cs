using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Bed.Runtime;

/// <summary>Formats optional saved-project diagnostic locations for BED's Play console. It performs no I/O.</summary>
public static class ProjectDiagnostic
{
    private static readonly JsonSerializerOptions Options=new(){Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    private static readonly UTF8Encoding Utf8=new(false,true);
    /// <summary>Returns one BED_DIAGNOSTIC_V1 line, at most 4096 UTF-8 bytes. The game chooses where to print it.</summary>
    /// <param name="file">Project-root-relative forward-slash path, without rooted, empty, dot or dot-dot segments.</param>
    /// <param name="pointer">RFC 6901 JSON Pointer; empty means the document root.</param>
    /// <param name="message">Human-readable diagnostic message.</param>
    /// <returns>A single line with exactly file, pointer and message string fields.</returns>
    /// <exception cref="ArgumentException">An argument is invalid or the formatted line exceeds the byte limit.</exception>
    public static string Format(string file,string pointer,string message)
    {
        ArgumentNullException.ThrowIfNull(file);ArgumentNullException.ThrowIfNull(pointer);ArgumentNullException.ThrowIfNull(message);
        if(file.Length==0||file.Any(c=>char.IsControl(c)||c is ':' or '\\')||file.Split('/').Any(p=>p is "" or "." or ".."))
            throw new ArgumentException("Use a project-relative forward-slash file path without dot segments.",nameof(file));
        if(pointer.Length>0&&pointer[0]!='/')throw new ArgumentException("A JSON Pointer is empty or starts with /.",nameof(pointer));
        for(var i=0;i<pointer.Length;i++)
            if(pointer[i]=='~'&&(++i==pointer.Length||pointer[i] is not ('0' or '1')))
                throw new ArgumentException("A JSON Pointer escapes ~ as ~0 and / as ~1.",nameof(pointer));
        if((long)Utf8.GetByteCount(file)+Utf8.GetByteCount(pointer)+Utf8.GetByteCount(message)>4096)
            throw new ArgumentException("A diagnostic line must fit in 4096 UTF-8 bytes.");
        var result="BED_DIAGNOSTIC_V1 "+JsonSerializer.Serialize(new{file,pointer,message},Options);
        if(Utf8.GetByteCount(result)>4096)throw new ArgumentException("A diagnostic line must fit in 4096 UTF-8 bytes.");
        return result;
    }
}
