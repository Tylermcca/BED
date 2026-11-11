using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bed.Data;
namespace Bed.Editor;

/// <summary>One authoritative version-two scene editor and history for an explicitly bound admitted file.</summary>
public sealed class Scene2DocumentEntry
{
    internal Scene2DocumentEntry(DocumentSession<Scene2> session)=>Editor=new(session,Scene2Editing.Outline);
    /// <summary>The version-two authority; effective objects are resolved views, never another writable session.</summary>
    public EditorModel<Scene2> Editor {get;}
    /// <summary>The same session owned by Editor. Use DocumentHost for checked save and source operations.</summary>
    public DocumentSession<Scene2> Session=>Editor.Session;
}

/// <summary>Read-only Outliner policy for immutable scene-v2 authoring documents.</summary>
public static class Scene2Editing
{
    private sealed class Index(Scene2 scene)
    {internal readonly Scene2Resolution Resolved=Scene2Resolver.Resolve(scene);internal Dictionary<string,SceneObject>? Objects;internal Dictionary<string,string>? Labels;}
    private static readonly ConditionalWeakTable<Scene2,Index> indexes=new();
    private static Index For(Scene2 scene)=>indexes.GetValue(scene,s=>new(s));
    /// <summary>Effective object IDs, labels and parents, in resolved authoritative order.</summary>
    public static IDocumentOutline<Scene2> Outline {get;}=new OutlinePolicy();
    private sealed class OutlinePolicy:IDocumentOutline<Scene2>
    {
        public long? Version=>0;
        private static SceneObject? Find(Scene2 scene,string id)
        {var index=For(scene);index.Objects??=index.Resolved.Objects.ToDictionary(o=>o.Object.Id,o=>o.Object,StringComparer.Ordinal);return index.Objects.GetValueOrDefault(id);}
        public IEnumerable<string> ObjectIds(Scene2 scene)=>For(scene).Resolved.Objects.Select(o=>o.Object.Id);
        public string? Parent(Scene2 scene,string id)=>Find(scene,id)?.Parent;
        public string? Label(Scene2 scene,string id)
        {
            var index=For(scene);
            if(index.Labels is null)
            {
                var instances=scene.Instances.ToDictionary(i=>i.Key,StringComparer.Ordinal);
                var names=scene.Bases.ToDictionary(b=>b.Id,b=>b.Definition.Nodes.ToDictionary(n=>n.Key,n=>n.Label,StringComparer.Ordinal),StringComparer.Ordinal);
                index.Labels=new(StringComparer.Ordinal);
                foreach(var value in index.Resolved.Objects)
                {
                    var item=value.Object;var origin=index.Resolved.Origins[item.Id];
                    index.Labels[item.Id]=item.Properties.TryGetValue("name",out var name)&&name.ValueKind==JsonValueKind.String?name.GetString()!:
                        origin.Kind==Scene2OriginKind.PrefabNode?names[instances[origin.InstanceKey!].Base][origin.NodeKey!]:item.Id;
                }
            }
            return index.Labels.GetValueOrDefault(id);
        }
    }
}

/// <summary>One admitted saved prefab source, suitable for a stable native picker. Contains no filesystem path or source bytes.</summary>
/// <param name="Name">Canonical admitted project-relative name.</param>
/// <param name="Guid">Permanent prefab identity.</param>
/// <param name="Space">Accepted coordinate profile.</param>
public sealed record PrefabSourceInfo(string Name,Guid Guid,SceneSpace Space)
{
    /// <summary>Immutable exposed reference slots in definition order, populated by DocumentHost.ListPrefabSources. Empty for a manually constructed descriptor.</summary>
    public IReadOnlyList<PrefabSlot> Slots {get;}=[];
    internal PrefabSourceInfo(string name,Guid guid,SceneSpace space,IEnumerable<PrefabSlot> slots):this(name,guid,space)=>Slots=Array.AsReadOnly(slots.ToArray());
}

public sealed partial class DocumentHost
{
    /// <summary>Lists the same bounded, safely admitted saved source registry used by instantiate/update, ordered by name then GUID.
    /// Dirty definitions, duplicate identities, invalid definitions and unsafe read sets refuse the whole list. No paths or bytes escape.</summary>
    public bool ListPrefabSources(out IReadOnlyList<PrefabSourceInfo> sources,out string why)
    {
        sources=[];
        try
        {
            sources=Array.AsReadOnly(Scene2Source.All(project).Select(s=>new PrefabSourceInfo(s.Name,Guid.ParseExact(s.Definition.Guid,"N"),s.Definition.Space,s.Definition.Slots))
                .OrderBy(s=>s.Name,StringComparer.Ordinal).ThenBy(s=>s.Guid).ToArray());why="";return true;
        }
        catch(Exception e)when(Scene2Source.Expected(e)){why="Prefab source list refused: "+e.Message;return false;}
    }
    private readonly Dictionary<string,Scene2DocumentEntry> scene2=new(StringComparer.Ordinal);
    private sealed record Admission(string Name,Scene2DocumentEntry Entry,Scene2Source? Source);
    private readonly ConditionalWeakTable<Scene2EditPlan,Admission> scene2Plans=new();
    /// <summary>Optional typed version-two save policy. A legacy data save rule requires an explicit typed policy.</summary>
    public Func<Scene2DocumentEntry,string?>? Scene2SaveCheck {get;set;}
    /// <summary>Gets the existing authoritative entry without reading or opening a file.</summary>
    public bool TryGetOpenScene2(string name,out Scene2DocumentEntry? entry)=>scene2.TryGetValue(name,out entry);
    /// <summary>Opens an explicitly scene-bound version-two file. V1/custom kinds and failed reads never publish a v2 entry.</summary>
    public bool OpenScene2(string name,out Scene2DocumentEntry? entry,out string why)
    {
        entry=null;
        if(!bindings.TryGetValue(name,out var kind)||kind!="scene"){why=$"{name} has no explicit scene binding.";return false;}
        if(scenes.ContainsKey(name)){why=$"{name} already has a version-one authority; use OpenScene.";return false;}
        if(scene2.TryGetValue(name,out entry)){why="";return true;}
        if(project.Find(name) is not {} file){why=$"There's no admitted file called {name}.";return false;}
        if(!opening.Add(name)){why=$"{name} is already opening.";return false;}
        try
        {
            var text=Scene2Source.Read(project,file,32*1024*1024);_=new Scene2Format().Read(text);
            var session=DocumentSession<Scene2>.Open(file.Path,new Scene2Format(),new Scene2Validator(),Scene2Kit.Catalog());
            session.FileAccessRefusal=path=>Path.GetFullPath(path)!=Path.GetFullPath(file.Path)?"Use the admitted typed save path.":Scene2Source.PathRefusal(path)??project.FileAccessRefusal(file.Name)??project.ReadOnlyReason(Path.GetRelativePath(project.DataFolder,path).Replace('\\','/'));
            entry=new(session);entry.Editor.PlayRefusal=()=>"Use project Build and Play to build this level and run the saved game. Configure the commands in Tools > Play settings.";
            scene2.Add(name,entry);why="";return true;
        }
        catch(Exception e)when(Scene2Source.Expected(e)){why=$"{name} can't be opened as scene v2: {e.Message}";return false;}
        finally{opening.Remove(name);}
    }
    private string? Scene2Refusal(string name,Scene2DocumentEntry entry)
    {
        if(checking||project.Judging)return "A save check is in progress.";
        if(!scene2.TryGetValue(name,out var current)||!ReferenceEquals(current,entry))return "The scene-v2 admission changed; reopen it.";
        if(entry.Editor.Playing is not null)return "Stop Play before editing or saving.";
        if(project.Find(name) is not {} file||!File.Exists(file.Path))return "The scene file is no longer safely admitted.";
        return project.FileAccessRefusal(name)??Scene2Source.PathRefusal(file.Path)??project.ReadOnlyReason(name);
    }
    /// <summary>Saves the typed draft after validation, policy, current admission/revision and exact saved-disk conflict checks.</summary>
    public bool SaveScene2(string name,out string why)
    {
        if(!scene2.TryGetValue(name,out var entry)){why=$"{name} has no open scene-v2 session.";return false;}
        if(Scene2Refusal(name,entry) is {} refusal){why=refusal;return false;}
        if(project.SaveCheck is not null&&Scene2SaveCheck is null){why="An explicit scene-v2 save policy is required.";return false;}
        var session=entry.Session;var revision=session.Revision;var policy=Scene2SaveCheck;checking=true;
        try
        {
            var issues=session.Validator.Validate(session.Current);if(issues.HasErrors()){why=string.Join(" ",issues.Select(i=>i.Message));return false;}
            if(policy?.Invoke(entry) is {} rejected){why=rejected;return false;}
            if(!scene2.TryGetValue(name,out var current)||!ReferenceEquals(current,entry)||session.Revision!=revision||!ReferenceEquals(policy,Scene2SaveCheck))
            {why="The scene or save policy changed during its check; save again.";return false;}
            if(project.Judging||project.Find(name) is not {} savedFile||Scene2Source.PathRefusal(savedFile.Path) is not null||project.FileAccessRefusal(name) is not null||project.ReadOnlyReason(name) is not null||entry.Editor.Playing is not null||project.SaveCheck is not null&&Scene2SaveCheck is null)
            {why="The scene admission, Play or save policy changed; nothing was saved.";return false;}
            if(session.CheckDisk()!=DiskState.Unchanged){why="The scene was changed or removed outside the editor; nothing was saved.";return false;}
            if(!session.Dirty){why="Nothing to save.";return true;}
            var text=session.TextToSave;var result=FileGroup.Write([new(session.FilePath!,session.ExpectedOnDisk,text)]);
            if(result.States[0]==GroupFileState.Written)session.MarkSaved(text);
            if(result.Failed is not null||result.States[0]!=GroupFileState.Written){why=result.Why??"Scene save failed; keep the draft.";return false;}
            why=$"Saved {name}.";return true;
        }
        catch(Exception e)when(e is not OutOfMemoryException){why="The scene-v2 save failed: "+e.Message;return false;}
        finally{checking=false;}
    }
    /// <summary>Closes only after Save or explicit Discard; cancellation or failed Save retains the exact draft and entry.</summary>
    public bool CloseScene2(string name,SceneCloseDecision decision,out string why)
    {
        if(checking||project.Judging){why="A save check is in progress; close is refused.";return false;}
        if(!scene2.TryGetValue(name,out var entry)){why=$"{name} has no open scene-v2 session.";return false;}
        if(decision is not(SceneCloseDecision.Save or SceneCloseDecision.Discard)){why="Close cancelled; choose Save or Discard.";return false;}
        if(decision==SceneCloseDecision.Save&&!SaveScene2(name,out why))return false;
        try{entry.Editor.Stop();}catch(Exception e){why="Play could not stop cleanly: "+e.Message;return false;}
        scene2.Remove(name);why=$"Closed {name}.";return true;
    }
    /// <summary>Previews one strict registered action, reading prefab content only by GUID from admitted saved source files.
    /// Generated keys and accepted source JSON are captured once in the resolved command; no draft or file is changed.</summary>
    public bool CaptureScene2Edit(string name,string action,JsonElement args,out Scene2EditPlan? plan,out string why)
    {
        plan=null;
        if(!scene2.TryGetValue(name,out var entry)){why="Open the admitted scene-v2 file first.";return false;}
        if(Scene2Refusal(name,entry) is {} refusal){why=refusal;return false;}
        if(entry.Session.CheckDisk()!=DiskState.Unchanged){why="The saved scene changed; resolve its disk conflict.";return false;}
        try
        {
            Scene2Source? source=null;ICommand<Scene2> command;
            if(action is "instantiate" or "update")
            {
                Scene2Source.CheckArguments(args,action=="instantiate"?["source","key","baseId","placement","nodeIds","bindings"]:["instance","baseId","newNodeIds"]);
                var node=JsonNode.Parse(args.GetRawText())!.AsObject();
                var guid=action=="instantiate"?node["source"]?.GetValue<string>():entry.Session.Current.Instances.SingleOrDefault(i=>i.Key==node["instance"]?.GetValue<string>())?.Source;
                if(guid is null)throw new ArgumentException("Choose an existing source GUID or instance.");
                source=Scene2Source.Find(project,guid);node.Remove("source");node["definition"]=JsonNode.Parse(source.Text);node["baseId"]??="base_"+Guid.NewGuid().ToString("N");
                if(action=="instantiate")
                {
                    node["key"]??=Guid.NewGuid().ToString("N");node["bindings"]??=new JsonObject();
                    node["nodeIds"]??=new JsonObject(source.Definition.Nodes.Select(n=>KeyValuePair.Create<string,JsonNode?>(n.Key,JsonValue.Create("node_"+Guid.NewGuid().ToString("N")))));
                }
                else
                {
                    var instance=entry.Session.Current.Instances.Single(i=>i.Key==node["instance"]!.GetValue<string>());
                    node["newNodeIds"]??=new JsonObject(source.Definition.Nodes.Where(n=>!instance.NodeIds.ContainsKey(n.Key)).Select(n=>KeyValuePair.Create<string,JsonNode?>(n.Key,JsonValue.Create("node_"+Guid.NewGuid().ToString("N")))));
                }
                command=Scene2Kit.Command(action,JsonSerializer.SerializeToElement(node));
            }
            else
            {
                if(action=="group")
                {
                    Scene2Source.CheckArguments(args,["id","ids"]);var node=JsonNode.Parse(args.GetRawText())!.AsObject();node["id"]??="group_"+Guid.NewGuid().ToString("N");args=JsonSerializer.SerializeToElement(node);
                }
                action=action switch{"add" or "move" or "rotate" or "scale" or "rename"=>"object."+action,"delete"=>"object.remove","duplicate" when args.TryGetProperty("ids",out _)=>"object.duplicate",_=>action};
                command=Scene2Kit.Command(action,args);
            }
            if(!Scene2EditPlan.Capture(entry.Session,command,out plan,out why))return false;
            scene2Plans.Add(plan!,new(name,entry,source));return true;
        }
        catch(Exception e)when(Scene2Source.Expected(e)){why="Scene-v2 edit refused: "+e.Message;return false;}
    }
    /// <summary>Rechecks this host/entry, editing guards and selected saved source bytes before one atomic plan commit.
    /// Stale, replaced, read-only, conflicting or unsafe source admissions leave the draft/history unchanged.</summary>
    public bool CommitScene2Edit(string name,Scene2EditPlan plan,out string why)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if(!scene2Plans.TryGetValue(plan,out var admission)||admission.Name!=name||!scene2.TryGetValue(name,out var entry)||!ReferenceEquals(entry,admission.Entry))
        {why="The plan belongs to another scene admission; preview again.";return false;}
        if(Scene2Refusal(name,entry) is {} refusal){why=refusal;return false;}
        try
        {
            if(admission.Source is {} accepted)
            {var current=Scene2Source.Find(project,accepted.Definition.Guid);if(current.Name!=accepted.Name||current.Hash!=accepted.Hash){why="The admitted source changed; preview again.";return false;}}
            return plan.Commit(entry.Session,out why);
        }
        catch(Exception e)when(Scene2Source.Expected(e)){why="Scene-v2 edit refused: "+e.Message;return false;}
    }
}

// Source discovery stays inside this admitted project's listed files. No caller-supplied path is read.
internal sealed record Scene2Source(string Name,string Text,PrefabDefinition Definition,string Hash)
{
    internal static bool Expected(Exception e)=>e is IOException or UnauthorizedAccessException or DocumentFormatException or InvalidDocumentException or JsonException or ArgumentException or InvalidOperationException;
    internal static void CheckArguments(JsonElement args,string[] allowed)
    {if(args.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Expected an argument object.");var p=args.EnumerateObject().ToArray();if(p.Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()!=p.Length||p.Any(x=>!allowed.Contains(x.Name,StringComparer.Ordinal)||x.Value.ValueKind==JsonValueKind.Null))throw new ArgumentException("Unknown or duplicate action argument.");}
    internal static string Read(DataProject project,DataFile file,int maximum)=>Read(project,file,maximum,out _);
    private static string Read(DataProject project,DataFile file,int maximum,out string hash)
    {
        if(project.FileAccessRefusal(file.Name) is {} admission)throw new IOException(file.Name+": "+admission);
        if(PathRefusal(file.Path) is {} unsafePath)throw new IOException(unsafePath);
        using var stream=new FileStream(file.Path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>maximum)throw new IOException(file.Name+" exceeds the bounded read limit.");
        using var buffer=new MemoryStream();stream.CopyTo(buffer);if(buffer.Length>maximum)throw new IOException("File changed beyond its read limit.");
        var owned=buffer.ToArray();hash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(owned));
        return new System.Text.UTF8Encoding(false,true).GetString(owned).TrimStart('\ufeff');
    }
    internal static string? PathRefusal(string path)
    {
        try
        {
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return "Scene paths cannot pass through a junction or symbolic link.";
            for(var parent=new FileInfo(path).Directory;parent is not null;parent=parent.Parent)
                if((parent.Attributes&FileAttributes.ReparsePoint)!=0)return "Scene paths cannot pass through a junction or symbolic link.";
            return null;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){return "The scene path cannot be accessed: "+e.Message;}
    }
    internal static Scene2Source Find(DataProject project,string guid)
    {
        if(guid.Length!=32||guid.Any(c=>c is not(>= '0' and <= '9' or >= 'a' and <= 'f')))throw new ArgumentException("Use a lowercase prefab source GUID, never a source path.");
        return All(project).SingleOrDefault(s=>s.Definition.Guid==guid)??throw new IOException("No admitted saved prefab definition has GUID "+guid+".");
    }
    internal static IReadOnlyList<Scene2Source> All(DataProject project)
    {
        var sources=new Dictionary<string,Scene2Source>(StringComparer.Ordinal);long bytes=0;var count=0;
        project.Refresh();
        foreach(var file in project.Files.Where(f=>f.State!=DataFileState.Missing))
        {
            if(++count>10000)throw new IOException("Prefab discovery exceeded 10,000 admitted files.");
            var text=Read(project,file,32*1024*1024,out var hash);bytes+=System.Text.Encoding.UTF8.GetByteCount(text);if(bytes>64*1024*1024)throw new IOException("Prefab discovery exceeded its 64 MiB read budget.");
            JsonDocument json;try{json=JsonDocument.Parse(text,new(){MaxDepth=64});}catch(JsonException){continue;}
            using(json)
            {
                if(json.RootElement.ValueKind!=JsonValueKind.Object||!json.RootElement.TryGetProperty("kind",out var kind)||kind.ValueKind!=JsonValueKind.String||kind.GetString()!="prefab-definition")continue;
                if(file.Dirty)throw new IOException(file.Name+" has unsaved source edits; save it before accepting an update.");
                var definition=new PrefabDefinitionFormat().Read(text);
                if(!sources.TryAdd(definition.Guid,new(file.Name,text,definition,hash)))throw new IOException("Duplicate admitted prefab GUID "+definition.Guid+".");
            }
        }
        return sources.Values.ToArray();
    }
}
