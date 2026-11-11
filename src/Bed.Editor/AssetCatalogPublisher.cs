using System.Security.Cryptography;
using System.Text.Json;

namespace Bed.Editor;

/// <summary>Single-owner staged cache storage. No importer, live-source scan, policy admission or artifact eviction.</summary>
public sealed class AssetCatalogPublisher : IDisposable
{
    private const string Marker = "BED asset cache stage v1";
    private readonly object gate = new();
    private readonly string cache;
    private readonly FileStream ownership;
    private Snapshot? current;
    private bool closed;

    /// <summary>A verified stored artifact, relative to its immutable generation directory.</summary>
    public sealed record Artifact(string Name, long Length, string Sha256);
    /// <summary>The storage manifest; Revision must identify an already-admitted complete input/content set.</summary>
    public sealed record Snapshot(string Id, long Generation, string Revision, IReadOnlyList<Artifact> Artifacts);
    private sealed record Pointer(string Id, string ManifestHash);

    /// <summary>Opaque prepared allocation belonging only to its creating publisher.</summary>
    public sealed class Staged
    {
        internal Staged(AssetCatalogPublisher owner, string id, long expected, Snapshot manifest,Snapshot? previous)
        { Owner = owner; Id = id; Expected = expected; Manifest = manifest;Previous=previous; }
        internal AssetCatalogPublisher Owner { get; }
        internal string Id { get; }
        internal long Expected { get; }
        internal Snapshot Manifest { get; }
        internal bool Canceled;
        internal bool Finished;
        internal Snapshot? Previous {get;}
        internal bool RolledBack;
    }

    /// <summary>Opens only project/.bed/cache, exclusively; validates the published pointer before recovering owned stages.</summary>
    public AssetCatalogPublisher(string projectRoot)
    {
        var root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        Safe(root);
        cache = Path.Combine(root, ".bed", "cache");
        Safe(cache); Directory.CreateDirectory(cache);
        Safe(Path.Combine(cache,"owner.lock"));
        ownership = new FileStream(Path.Combine(cache,"owner.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try
        {
            Safe(cache);
            Safe(Path.Combine(cache,"staging")); Safe(Path.Combine(cache,"generations"));
            Directory.CreateDirectory(Path.Combine(cache,"staging"));
            Directory.CreateDirectory(Path.Combine(cache,"generations"));
            current = ReadCurrent();
            foreach (var directory in Directory.EnumerateDirectories(Path.Combine(cache,"staging")))
            {
                if (!IsId(Path.GetFileName(directory))) continue;
                SafeTree(directory);
                var marker = Path.Combine(directory,"owner.txt");
                if (File.Exists(marker) && File.ReadAllText(marker) == Marker) DeleteOwnedStage(directory);
            }
        }
        catch { ownership.Dispose(); throw; }
    }

    /// <summary>The verified published storage manifest, or null. Returned collections cannot mutate publisher state.</summary>
    public Snapshot? Current { get { lock(gate) return current; } }

    /// <summary>Copies the supplied complete output set into owned staging. No source path or importer is accepted.</summary>
    public Staged Prepare(string revision, IReadOnlyDictionary<string, byte[]> artifacts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision); ArgumentNullException.ThrowIfNull(artifacts);
        lock(gate)
        {
            Open(); Safe(cache); Safe(Path.Combine(cache,"staging"));
            var copies = artifacts.Select(p => (p.Key, Bytes: p.Value?.ToArray() ?? throw new ArgumentException("Null artifact."))).ToArray();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var item in copies)
                if(!ValidName(item.Key) || !names.Add(item.Key)) throw new ArgumentException("Artifact names must be unique simple names, not paths or reserved names.");
            var id = Guid.NewGuid().ToString("N");
            var dir = Path.Combine(cache,"staging",id); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir,"owner.txt"),Marker);
            var entries = new List<Artifact>();
            foreach(var item in copies.OrderBy(p=>p.Key,StringComparer.Ordinal))
            {
                WriteFlushed(Path.Combine(dir,item.Key),item.Bytes);
                entries.Add(new(item.Key,item.Bytes.LongLength,Hash(item.Bytes)));
            }
            var manifest = new Snapshot(id,checked((current?.Generation ?? 0)+1),revision,entries.AsReadOnly());
            WriteFlushed(Path.Combine(dir,"manifest.json"),JsonSerializer.SerializeToUtf8Bytes(manifest));
            return new(this,id,current?.Generation ?? 0,manifest,current);
        }
    }

    /// <summary>Cancellation is serialized with publication. Before replacement it wins; afterward completion remains committed.</summary>
    public bool Cancel(Staged stage)
    {
        lock(gate)
        {
            if(closed || !ReferenceEquals(stage.Owner,this) || stage.Finished) return false;
            stage.Canceled = true; return true;
        }
    }

    /// <summary>Replaces one pointer after complete artifact verification. Failure/cancel never updates the previous pointer.</summary>
    public bool Publish(Staged stage, out string why)
    {
        lock(gate)
        {
            if(closed || !ReferenceEquals(stage.Owner,this) || stage.Finished || stage.Canceled)
            { why="The stage is closed, foreign, already completed or canceled.";return false; }
            if(stage.Expected != (current?.Generation ?? 0))
            { why="The published predecessor changed; prepare a new complete snapshot.";return false; }
            Safe(cache);
            var from=Path.Combine(cache,"staging",stage.Id);
            var verified=ReadManifest(from,stage.Id);
            if(JsonSerializer.Serialize(verified)!=JsonSerializer.Serialize(stage.Manifest)) throw new InvalidDataException("Staged manifest changed.");
            var destination=Path.Combine(cache,"generations",stage.Id); Safe(destination);
            Directory.Move(from,destination);
            var bytes=JsonSerializer.SerializeToUtf8Bytes(new Pointer(stage.Id,Hash(File.ReadAllBytes(Path.Combine(destination,"manifest.json")))));
            var temp=Path.Combine(cache,"pointer-"+stage.Id+".tmp");WriteFlushed(temp,bytes);
            var pointer=Path.Combine(cache,"current.json"); Safe(pointer);
            // Same directory/volume; only this replacement publishes the generation. No cross-file atomicity claim.
            if(File.Exists(pointer)) File.Replace(temp,pointer,null); else File.Move(temp,pointer);
            current=verified;stage.Finished=true;why="";return true;
        }
    }

    /// <summary>Restores only this completed stage's captured predecessor while its pointer is still current.
    /// No generation is deleted; failure is explicit and never overwrites a newer publication.</summary>
    public bool Rollback(Staged stage,out string why)
    {
        ArgumentNullException.ThrowIfNull(stage);
        lock(gate)
        {
            try
            {
                Open();
                if(!ReferenceEquals(stage.Owner,this)||!stage.Finished||stage.RolledBack||current?.Id!=stage.Id||ReadCurrent()?.Id!=stage.Id)
                {why="The completed stage is foreign, already undone or no longer current.";return false;}
                var pointer=Path.Combine(cache,"current.json");Safe(pointer);
                if(stage.Previous is { } previous)
                {
                    var directory=Path.Combine(cache,"generations",previous.Id);var verified=ReadManifest(directory,previous.Id);
                    if(JsonSerializer.Serialize(verified)!=JsonSerializer.Serialize(previous))throw new InvalidDataException("The predecessor generation changed.");
                    var bytes=JsonSerializer.SerializeToUtf8Bytes(new Pointer(previous.Id,Hash(File.ReadAllBytes(Path.Combine(directory,"manifest.json")))));
                    var temp=Path.Combine(cache,"pointer-"+Guid.NewGuid().ToString("N")+".tmp");WriteFlushed(temp,bytes);File.Replace(temp,pointer,null);
                }
                else File.Delete(pointer);
                current=stage.Previous;stage.RolledBack=true;why="";return true;
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {why=e.Message;return false;}
        }
    }

    private Snapshot? ReadCurrent()
    {
        var path=Path.Combine(cache,"current.json");Safe(path);
        if(!File.Exists(path))return null;
        var pointer=JsonSerializer.Deserialize<Pointer>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Empty cache pointer.");
        if(!IsId(pointer.Id))throw new InvalidDataException("Invalid cache generation identity.");
        var dir=Path.Combine(cache,"generations",pointer.Id);SafeTree(dir);
        if(Hash(File.ReadAllBytes(Path.Combine(dir,"manifest.json")))!=pointer.ManifestHash)throw new InvalidDataException("Published manifest changed.");
        return ReadManifest(dir,pointer.Id);
    }

    private static Snapshot ReadManifest(string dir,string id)
    {
        SafeTree(dir);
        if(File.ReadAllText(Path.Combine(dir,"owner.txt"))!=Marker)throw new InvalidDataException("Unowned cache allocation.");
        var manifest=JsonSerializer.Deserialize<Snapshot>(File.ReadAllBytes(Path.Combine(dir,"manifest.json"))) ?? throw new InvalidDataException("Empty manifest.");
        if(manifest.Id!=id || manifest.Generation<1 || string.IsNullOrWhiteSpace(manifest.Revision) || manifest.Artifacts is null)
            throw new InvalidDataException("Invalid manifest header.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"owner.txt","manifest.json"};
        foreach(var artifact in manifest.Artifacts)
        {
            if(artifact is null || !ValidName(artifact.Name) || !names.Add(artifact.Name))throw new InvalidDataException("Invalid artifact name.");
            var bytes=File.ReadAllBytes(Path.Combine(dir,artifact.Name));
            if(bytes.LongLength!=artifact.Length || Hash(bytes)!=artifact.Sha256)throw new InvalidDataException("Incomplete or changed artifact: "+artifact.Name);
        }
        if(Directory.EnumerateDirectories(dir).Any() || Directory.EnumerateFiles(dir).Any(p=>!names.Contains(Path.GetFileName(p))))
            throw new InvalidDataException("Undeclared staged content.");
        return manifest with { Artifacts=Array.AsReadOnly(manifest.Artifacts.ToArray()) };
    }

    private void DeleteOwnedStage(string dir)
    {
        var full=Path.GetFullPath(dir);var parent=Path.GetFullPath(Path.Combine(cache,"staging"));
        if(!string.Equals(Path.GetDirectoryName(full),parent,StringComparison.OrdinalIgnoreCase) || !IsId(Path.GetFileName(full)))
            throw new InvalidDataException("Stage is outside owned staging.");
        SafeTree(full);Directory.Delete(full,true);
    }
    private static bool IsId(string? id)=>id is {Length:32} && id.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool ValidName(string? name)=>name is {Length:>0 and <=128} && name is not "." and not ".."
        && name.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '_' or '-')
        && !name.Equals("owner.txt",StringComparison.OrdinalIgnoreCase) && !name.Equals("manifest.json",StringComparison.OrdinalIgnoreCase)
        && !name.EndsWith('.') && !DeviceName(name.Split('.')[0]);
    private static bool DeviceName(string stem)
    {
        var name=stem.ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL"
            || name.Length==4 && (name.StartsWith("COM",StringComparison.Ordinal)||name.StartsWith("LPT",StringComparison.Ordinal))
                && name[3] is >= '1' and <= '9';
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void WriteFlushed(string path,byte[] bytes)
    { Safe(path);using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);file.Write(bytes);file.Flush(true); }
    private static void Safe(string path)
    {
        for(var p=Path.GetFullPath(path);p is not null;p=Path.GetDirectoryName(p))
        {
            FileAttributes attributes;
            try { attributes=File.GetAttributes(p); }
            catch(FileNotFoundException) { continue; }
            catch(DirectoryNotFoundException) { continue; }
            if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked cache paths are not supported.");
        }
    }
    private static void SafeTree(string directory)
    {
        Safe(directory);
        foreach(var entry in Directory.EnumerateFileSystemEntries(directory))
        { Safe(entry);if(Directory.Exists(entry))SafeTree(entry); }
    }
    private void Open()=>ObjectDisposedException.ThrowIf(closed,this);
    /// <summary>Closes the admission without deleting published content. Unpublished owned stages recover on the next open.</summary>
    public void Dispose(){lock(gate){if(closed)return;closed=true;ownership.Dispose();}}
}
