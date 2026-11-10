using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Brutal.Authoring.Editor.ImGui;

// Per-user view choices, never scene content, command arguments or saved-game input.
internal sealed class SceneEditorFlags
{
    private readonly HashSet<string> hidden=new(StringComparer.Ordinal),locked=new(StringComparer.Ordinal);
    private Scene? indexed;private Dictionary<string,SceneObject> objects=[];
    private string? file;
    private sealed record Saved(int Version,string[] Hidden,string[] Locked);
    internal string Note{get;private set;}="";
    private Dictionary<string,SceneObject> Index(Scene scene)
    {
        if(!ReferenceEquals(indexed,scene)){objects=scene.Objects.GroupBy(o=>o.Id,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.Ordinal);indexed=scene;}
        return objects;
    }
    private bool Effective(Scene scene,string id,HashSet<string> flags)
    {
        if(flags.Count==0)return false;
        var map=Index(scene);var visited=new HashSet<string>(StringComparer.Ordinal);
        for(string? at=id;at is not null&&visited.Add(at);at=map.TryGetValue(at,out var item)?item.Parent:null)
            if(flags.Contains(at))return true;
        return false;
    }
    internal bool Hidden(Scene scene,string id)=>Effective(scene,id,hidden);
    internal bool Locked(Scene scene,string id)=>Effective(scene,id,locked);
    internal string Badge(Scene scene,string id)=>(Hidden(scene,id)?" [hidden]":"")+(Locked(scene,id)?" [locked]":"");
    internal void Bind(string project,string level,string preferences)
    {
        var key=Path.TrimEndingDirectorySeparator(Path.GetFullPath(project));if(OperatingSystem.IsWindows())key=key.ToUpperInvariant();
        var projectKey=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var levelKey=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows()?level.ToUpperInvariant():level)));
        file=Path.Combine(Path.GetFullPath(preferences),"scene-views",projectKey,levelKey+".json");
        try
        {
            if(!File.Exists(file))return;
            if(new FileInfo(file).Length>4*1024*1024)throw new JsonException("View settings are too large.");
            var saved=JsonSerializer.Deserialize<Saved>(File.ReadAllText(file));
            if(saved is not{Version:1,Hidden:{},Locked:{}}||saved.Hidden.Any(string.IsNullOrEmpty)||saved.Locked.Any(string.IsNullOrEmpty))throw new JsonException("Invalid view settings.");
            hidden.Clear();hidden.UnionWith(saved.Hidden);locked.Clear();locked.UnionWith(saved.Locked);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException)
        {hidden.Clear();locked.Clear();Note="Could not read saved view choices; this level starts visible and unlocked. "+error.Message;}
    }
    internal (bool Ok,string Message) Toggle(Scene scene,IReadOnlyList<string> selection,bool hide)
    {
        if(selection.Count==0||selection.Any(id=>!Index(scene).ContainsKey(id)))return(false,"Select existing objects first.");
        var flags=hide?hidden:locked;var remove=selection.All(flags.Contains);
        foreach(var id in selection){if(remove)flags.Remove(id);else flags.Add(id);}
        Note="";
        if(file is not null)
        {
            try
            {
                var json=JsonSerializer.Serialize(new Saved(1,hidden.Order(StringComparer.Ordinal).ToArray(),locked.Order(StringComparer.Ordinal).ToArray()));
                if(Encoding.UTF8.GetByteCount(json)>4*1024*1024)throw new IOException("View settings are too large.");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file+".tmp",json);File.Move(file+".tmp",file,true);
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException)
            {Note="Could not remember these view choices for next time. "+error.Message;}
        }
        return(true,$"{(hide?(remove?"Shown":"Hidden"):(remove?"Unlocked":"Locked"))} {selection.Count} object(s) in the editor. "+Note);
    }
    internal string? TransformRefusal(Scene scene,IReadOnlyList<string> selection)
    {
        if(locked.Count==0||selection.Count==0)return null;
        var selected=selection.ToHashSet(StringComparer.Ordinal);var map=Index(scene);
        foreach(var item in scene.Objects)
        {
            if(!Locked(scene,item.Id))continue;
            var seen=new HashSet<string>(StringComparer.Ordinal);
            for(string? at=item.Id;at is not null&&seen.Add(at);at=map.TryGetValue(at,out var parent)?parent.Parent:null)
                if(selected.Contains(at))return $"'{item.Id}' is locked; unlock it in the Outliner before moving or transforming it.";
        }
        return null;
    }
    // A locked parent's descendants are also protected; moving an ancestor may change a locked child's world pose.
    internal string? Refusal(Scene before,IReadOnlyList<ICommand<Scene>> commands)
    {
        if(locked.Count==0)return null;
        var protectedIds=before.Objects.Where(o=>Locked(before,o.Id)).Select(o=>o.Id).ToArray();if(protectedIds.Length==0)return null;
        // Built-in commands declare their affected identity: do not replay a large unrelated move just to check a lock.
        var ancestors=new Dictionary<string,string>(StringComparer.Ordinal);var map=Index(before);
        foreach(var protectedId in protectedIds)
        {
            var seen=new HashSet<string>(StringComparer.Ordinal);
            for(string? at=protectedId;at is not null&&seen.Add(at);at=map.TryGetValue(at,out var parent)?parent.Parent:null)
                ancestors.TryAdd(at,protectedId);
        }
        var unknown=false;
        foreach(var command in commands)
        {
            var affected=command switch
            {
                MoveObject move=>move.Id,RotateObject rotate=>rotate.Id,ScaleObject scale=>scale.Id,SetParent parent=>parent.Id,
                RemoveObject remove=>remove.Id,RenameObject rename=>rename.Id,
                AddObject or DuplicateGroup or SetSceneProperty or SetAsset or SetReference=>null,
                _=>Unknown()
            };
            string? Unknown(){unknown=true;return null;}
            if(affected is not null&&ancestors.TryGetValue(affected,out var blocked))
                return $"'{blocked}' is locked; unlock it in the Outliner before moving, transforming, reparenting or removing it.";
        }
        if(!unknown)return null;
        var after=before;
        foreach(var command in commands)
        {var result=command.Apply(after);if(!result.Ok)return null;after=result.Document!;}
        if(new SceneValidator().Validate(after).Any(i=>i.Severity==Severity.Error))return null;
        foreach(var id in protectedIds)
        {
            var old=before.Find(id)!;var current=after.Find(id);
            if(current is null||old.Position!=current.Position||old.Rotation!=current.Rotation||old.Scale!=current.Scale||old.Parent!=current.Parent
                ||SceneViewport.World(before,id)!=SceneViewport.World(after,id))
                return $"'{id}' is locked; unlock it in the Outliner before moving, transforming, reparenting or removing it.";
        }
        return null;
    }
}
