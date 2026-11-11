using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Bed;

/// <summary>Opt-in content freshness for a project-owned build. Reads only declared root-contained files; never executes a program or grants admission.</summary>
public static class ProjectBuildStamp
{
    /// <summary>Maximum file/directory entries scanned, and therefore maximum selected files, per fingerprint.</summary>
    public const int MaxFiles=100000;
    /// <summary>Maximum combined bytes of selected inputs, and separately of checked outputs: 256 MiB.</summary>
    public const long MaxTotalBytes=256L*1024*1024;
    /// <summary>Maximum encoded stamp size: one MiB.</summary>
    public const int MaxStampBytes=1024*1024;
    private const int MaxPatterns=256,MaxOutputs=1024;
    private static readonly StringComparer Names=OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;
    private static string[] Patterns(IEnumerable<string> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);var owned=inputs.Take(MaxPatterns+1).Select(p=>p?.Replace('\\','/')??throw new ArgumentException("Build input patterns cannot be null.")).ToArray();
        if(owned.Length>MaxPatterns)throw new ArgumentException("Build input patterns exceed 256 entries.");
        foreach(var p in owned)Portable(p,true);return owned;
    }
    internal static void Portable(string name,bool pattern=false)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>1024||name.Contains('\\')||name.Contains(':')||name.Any(c=>char.IsControl(c)||c is '<' or '>' or '|' or '"')||name.StartsWith('/')||(!pattern&&name.IndexOfAny(['*','?'])>=0))throw new ArgumentException("Use a bounded portable relative file path.");
        foreach(var part in name.Split('/'))
        {
            if(part.Length==0||part is "." or ".."||part.EndsWith(' ')||part.EndsWith('.'))throw new ArgumentException("Empty, dot and trailing-space/dot path segments are refused.");
            var stem=part.Split('.')[0].ToUpperInvariant();
            if(stem is "CON" or "PRN" or "AUX" or "NUL"||(stem.Length==4&&(stem.StartsWith("COM",StringComparison.Ordinal)||stem.StartsWith("LPT",StringComparison.Ordinal))&&char.IsNumber(stem[3])))throw new ArgumentException("Device path names are refused.");
        }
    }
    private static string Root(string root)
    {if(!Path.IsPathFullyQualified(root))throw new ArgumentException("Choose an absolute project root.");var full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));if(!Directory.Exists(full))throw new IOException("The project root does not exist.");NoLinks(full);return full;}
    private static void NoLinks(string path)
    {for(string? at=path;at is not null;at=Path.GetDirectoryName(at))if(Path.Exists(at)&&(File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("Build files cannot follow linked paths.");}
    private static string Inside(string root,string relative,bool rootAllowed=false)
    {
        if(!(rootAllowed&&relative=="."))Portable(relative);
        var full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!Names.Equals(full,root)&&!full.StartsWith(root+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new IOException("Build files must stay inside the project root.");
        NoLinks(full);return full;
    }
    private static Regex Match(string pattern)=>new("^"+Regex.Escape(pattern).Replace(@"\*\*/","(?:.*/)?").Replace(@"\*\*",".*").Replace(@"\*","[^/]*").Replace(@"\?","[^/]")+"$",RegexOptions.CultureInvariant|RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
    private static SortedDictionary<string,string> Files(string root,string[] patterns)
    {
        var files=new SortedDictionary<string,string>(StringComparer.Ordinal);var visited=0;
        foreach(var pattern in patterns)
        {
            var first=pattern.IndexOfAny(['*','?']);var slash=pattern.LastIndexOf('/',first<0?pattern.Length-1:first);var prefix=slash<0?".":pattern[..slash];
            var directory=Inside(root,prefix,true);var match=Match(pattern);var found=false;
            if(Directory.Exists(directory))
            {
                var stack=new Stack<string>();stack.Push(directory);
                while(stack.Count>0)
                {
                    var dir=stack.Pop();NoLinks(dir);
                    foreach(var path in Directory.EnumerateFiles(dir))
                    {
                        if(++visited>MaxFiles)throw new IOException("Build input scan exceeds 100000 entries.");
                        var name=Path.GetRelativePath(root,path).Replace('\\','/');if(!match.IsMatch(name))continue;
                        files[name]=Inside(root,name);found=true;
                    }
                    if(pattern.Contains('/'))foreach(var child in Directory.EnumerateDirectories(dir))
                    {if(++visited>MaxFiles)throw new IOException("Build input scan exceeds 100000 entries.");NoLinks(child);stack.Push(child);}
                }
            }
            if(first<0&&!found)throw new IOException("A declared build input is missing: "+pattern);
        }
        return files;
    }
    private static byte[] HashFile(string path,ref long total)
    {
        NoLinks(path);using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        total=checked(total+file.Length);if(total>MaxTotalBytes)throw new IOException("Build file bytes exceed 256 MiB.");
        return SHA256.HashData(file);
    }
    private static string FingerprintCore(string root,string[] patterns)
    {
        var files=Files(root,patterns);long total=0;using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);hash.AppendData("BED build inputs v1\n"u8);
        Span<byte> length=stackalloc byte[8];
        foreach(var (name,path) in files)
        {
            var bytes=Encoding.UTF8.GetBytes(name);BinaryPrimitives.WriteInt32LittleEndian(length,bytes.Length);hash.AppendData(length[..4]);hash.AppendData(bytes);
            var size=new FileInfo(path).Length;BinaryPrimitives.WriteInt64LittleEndian(length,size);hash.AppendData(length);hash.AppendData(HashFile(path,ref total));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    /// <summary>Hashes ordered portable names and contents from up to 256 declared globs. Refuses links, missing literal files, more than 100000 scanned entries/files or more than 256 MiB of selected input bytes.</summary>
    public static string Fingerprint(string root,IEnumerable<string> inputs)=>FingerprintCore(Root(root),Patterns(inputs));
    private static void Hex(string text)
    {if(text.Length!=64||text.Any(c=>c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))throw new ArgumentException("Expected a lowercase SHA256.");}
    private static void NotSelf(string stamp,string[] patterns)
    {if(patterns.Any(p=>Match(p).IsMatch(stamp)))throw new ArgumentException("A build stamp cannot include itself as an input.");}
    private sealed record Output(string Path,string Sha256);
    /// <summary>Rechecks the captured input hash, hashes 1..1024 explicit output files (at most 256 MiB), and atomically writes the bounded stamp last. Never writes inputs or outputs and never includes the stamp itself.</summary>
    public static void Write(string root,string stamp,IEnumerable<string> inputs,string expectedInputsHash,IEnumerable<string> outputs)
    {
        var full=Root(root);Portable(stamp);var patterns=Patterns(inputs);NotSelf(stamp,patterns);Hex(expectedInputsHash);
        if(FingerprintCore(full,patterns)!=expectedInputsHash)throw new IOException("Build inputs changed before publishing their stamp.");
        ArgumentNullException.ThrowIfNull(outputs);var names=outputs.Take(MaxOutputs+1).ToArray();
        if(names.Length is <1 or >MaxOutputs||names.Distinct(Names).Count()!=names.Length)throw new ArgumentException("Declare 1..1024 unique output files.");
        long total=0;var results=new List<Output>();foreach(var name in names.Order(StringComparer.Ordinal))
        {Portable(name);if(Names.Equals(name,stamp))throw new ArgumentException("A stamp cannot include itself as output.");results.Add(new(name,Convert.ToHexStringLower(HashFile(Inside(full,name),ref total))));}
        if(FingerprintCore(full,patterns)!=expectedInputsHash)throw new IOException("Build inputs changed during output capture.");
        var text=JsonSerializer.Serialize(new{version=1,inputsHash=expectedInputsHash,outputs=results.Select(o=>new{path=o.Path,sha256=o.Sha256})});
        if(Encoding.UTF8.GetByteCount(text)>MaxStampBytes)throw new IOException("Build stamp exceeds one MiB.");
        var target=Inside(full,stamp);Directory.CreateDirectory(Path.GetDirectoryName(target)!);Inside(full,stamp);AtomicFile.Write(target,text);
    }
    /// <summary>Checks a bounded stamp against current declared input content and explicit output hashes without executing or writing. Uses the same 100000-entry/256 MiB bounds; false supplies a plain refusal.</summary>
    public static bool Current(string root,string stamp,IEnumerable<string> inputs,out string why)
    {
        try
        {
            var full=Root(root);Portable(stamp);var patterns=Patterns(inputs);NotSelf(stamp,patterns);var path=Inside(full,stamp);
            byte[] bytes;using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
            {if(file.Length>MaxStampBytes)throw new IOException("Build stamp exceeds one MiB.");bytes=new byte[(int)file.Length];file.ReadExactly(bytes);}
            var r=new Scene2Read(Scene2Read.Parse(Scene2Read.Utf8.GetString(bytes),MaxStampBytes),"");r.Only("version","inputsHash","outputs");
            if(r.Get("version").Integer()!=1)throw new IOException("Unsupported build stamp version.");var expected=r.Get("inputsHash").Text(64,64);Hex(expected);
            if(FingerprintCore(full,patterns)!=expected)throw new IOException("Declared build input contents changed.");
            var outputs=r.Get("outputs").Array(MaxOutputs,true);var seen=new HashSet<string>(Names);long total=0;
            foreach(var output in outputs)
            {
                output.Only("path","sha256");var name=output.Get("path").Text(1,1024);Portable(name);
                if(Names.Equals(name,stamp)||!seen.Add(name))throw new IOException("Duplicate/self-referential output in build stamp.");var digest=output.Get("sha256").Text(64,64);Hex(digest);
                if(Convert.ToHexStringLower(HashFile(Inside(full,name),ref total))!=digest)throw new IOException("A declared build output changed: "+name);
            }
            why="";return true;
        }
        catch(Exception e)when(e is not OutOfMemoryException){why="Build stamp is unavailable or stale: "+e.Message;return false;}
    }
}
