using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Bed;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;

namespace SceneStarterAdapter;

/// <summary>Public catalog-pinned scene sample, connected to the declared standalone SceneStarter stdio peer.</summary>
public sealed partial class SceneStarterAdapter : IProjectAdapter
{
    private DataWorkspace? linkedWorkspace;
    private string? savedProjectRoot;
    public string Name=>"SceneStarter";
    public bool Recognises(ProjectPaths project)
    {
        if(project.Map is not {} map || !File.Exists(map)) return false;
        using var json=JsonDocument.Parse(File.ReadAllText(map));
        return json.RootElement.TryGetProperty("game"u8,out var game) && game.GetString()=="SceneStarter";
    }
    public DataProject Open(ProjectPaths project)
    {
        var scene=SceneSettings(ProjectRoot(project));
        var data=scene is null?DataProject.Open(project.Data,project.Schemas):DataProject.Open(project.Data,project.Schemas,recursive:true,admit:name=>!name.Split('/').Contains(".bed",StringComparer.OrdinalIgnoreCase));
        var name="level.json";
        if(scene is not null)
        {
            var path=Path.GetFullPath(Path.Combine(ProjectRoot(project),scene.Source));
            name=Path.GetRelativePath(Path.GetFullPath(project.Data),path).Replace('\\','/');
            if(name.StartsWith("../",StringComparison.Ordinal)||name==".."||Path.IsPathRooted(name))throw new InvalidOperationException("The level file must be inside the project's data folder.");
        }
        if(!DocumentHost.For(data).BindScene(name,"scene",out var why)) throw new InvalidOperationException(why);
        return data;
    }
    public void Configure(EditorModel<DataDocument> editor,DataTableView table) { }
    /// <summary>Ordinary saved-file Play from the project-owned game folder; no external program approval is implied.</summary>
    public PlaySettings DefaultPlay(ProjectPaths project)
    {
        var root=ProjectRoot(project);
        savedProjectRoot=root;
        if(SceneSettings(root) is {} scene)
        {
            var program=scene.SupportsPhysics?"game/PhysicsStarter.exe":"game/SceneStarter.exe";
            var inputs=scene.SupportsPhysics?SceneStarter.PhysicsRuntimeInventory.BuildInputs(scene.Source):[scene.Source,".bed/project.json",".bed/cache/current.json"];
            return new(program,["--project","."],".",new(program,["--compile-scene2","."],inputs){Stamp=".bed/generated/scenes/build.stamp.json"});
        }
        var source=Path.GetFullPath(Path.Combine(root,"..","SceneStarter","SceneStarter.csproj"));
        PlayBuildSettings? build=File.Exists(source)?new("dotnet",["build",source,"-c","Release","-o","game"],[]):null;
        return new("game/SceneStarter.exe",["--project","."],".",build);
    }
    private static string ProjectRoot(ProjectPaths project)=>Path.GetFullPath(project.Root??project.Folder??Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.Data)))!);
    private static Scene2BuildSettings? SceneSettings(string root)
    {
        var path=Path.Combine(root,".bed","project.json");
        for(string? at=path;at is not null;at=Path.GetDirectoryName(at))
            if(Path.Exists(at)&&(File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked scene build settings are refused.");
        if(!File.Exists(path))return null;
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        if(file.Length>Scene2BuildSettings.MaxProjectBytes)throw new IOException("Scene build settings exceed one MiB.");
        var bytes=new byte[checked((int)file.Length)];file.ReadExactly(bytes);return Scene2BuildSettings.ReadProject(new UTF8Encoding(false,true).GetString(bytes));
    }
    public GameLaunchSpec LinkedPlay(DataWorkspace workspace)
    {
        linkedWorkspace=workspace;
        var folder=Path.Combine(AppContext.BaseDirectory,"samples","SceneStarter");
        return new(Path.Combine(folder,"SceneStarter.exe"),folder,["--linked-play"]);
    }
    public PlaySnapshotConverter PlaySnapshot
    {
        get
        {
            // The launcher asks immediately after LinkedPlay(workspace); each stored converter owns that binding.
            var workspace=linkedWorkspace;
            return input=>ConvertExportInput(input,workspace);
        }
    }
    private sealed record Pin(string guid,string sourceHash,string contentRevision,string policyRevision,string derivedHash,byte[] derived);
    private static PlayConversion ConvertExportInput(PlaySnapshotDocument input,DataWorkspace? workspace)
    {
        const string legacy="scene-starter.scene.v1", assetsFormat="scene-starter.scene.v2";
        if(Encoding.UTF8.GetByteCount(input.Text)>1024*1024) return new(legacy,null,"The SceneStarter scene accepts up to one MiB.");
        var scene=SceneRuntimeReader.Read(input.Text).Scene;
        var ids=scene.Objects.Select(item=>item.Asset).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if(ids.Length==0) return new(legacy,Encoding.UTF8.GetBytes(input.Text));
        if(ids.Length>256) return new(assetsFormat,null,"SceneStarter accepts at most 256 pinned assets.");
        var catalog=workspace?.SceneAssets;
        if(catalog is null) return new(assetsFormat,null,"Open this project's assets before Play.");
        var selections=new List<AssetCatalog.Selection>();var pins=new List<Pin>();long total=0;
        foreach(var id in ids)
        {
            var selected=catalog.Select(id);
            if(selected is null || !catalog.TryResolve(selected,out var lease,out _))
                return new(assetsFormat,null,$"Asset {id} is missing, out of date or not allowed for Play.");
            total+=lease!.Length;
            if(total>64L*1024*1024) return new(assetsFormat,null,"SceneStarter accepts at most 64 MiB of derived asset bytes.");
            var bytes=new byte[lease.Length];lease.CopyTo(bytes);
            selections.Add(selected);
            pins.Add(new(id,lease.SourceHash,lease.ContentRevision,lease.PolicyRevision,Hash(bytes),bytes));
        }
        var payload=JsonSerializer.SerializeToUtf8Bytes(new {scene=input.Text,assets=pins});
        // Re-resolve all captured identities after copying/serialization, never fall back to paths or LastGood.
        for(var i=0;i<selections.Count;i++)
        {
            if(!catalog.TryResolve(selections[i],out var current,out _)) return new(assetsFormat,null,"The asset catalog changed while Play input was captured. Try again.");
            if(current!.Length!=pins[i].derived.Length) return new(assetsFormat,null,"Derived asset bytes changed while Play input was captured. Try again.");
            var bytes=new byte[current.Length];current.CopyTo(bytes);
            if(current.SourceHash!=pins[i].sourceHash || Hash(bytes)!=pins[i].derivedHash)
                return new(assetsFormat,null,"Derived asset bytes changed while Play input was captured. Try again.");
        }
        return new(assetsFormat,payload);
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
}
