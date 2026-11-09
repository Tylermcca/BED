using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;

namespace SceneStarterAdapter;

/// <summary>Public catalog-pinned scene sample, connected to the declared standalone SceneStarter stdio peer.</summary>
public sealed class SceneStarterAdapter : IProjectAdapter
{
    private DataWorkspace? linkedWorkspace;
    public string Name=>"SceneStarter";
    public bool Recognises(ProjectPaths project)
    {
        if(project.Map is not {} map || !File.Exists(map)) return false;
        using var json=JsonDocument.Parse(File.ReadAllText(map));
        return json.RootElement.TryGetProperty("game"u8,out var game) && game.GetString()=="SceneStarter";
    }
    public DataProject Open(ProjectPaths project)
    {
        var data=DataProject.Open(project.Data,project.Schemas);
        if(!DocumentHost.For(data).BindScene("level.json","scene",out var why)) throw new InvalidOperationException(why);
        return data;
    }
    public void Configure(EditorModel<DataDocument> editor,DataTableView table) { }
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
            return input=>ConvertInput(input,workspace);
        }
    }
    private sealed record Pin(string guid,string sourceHash,string contentRevision,string policyRevision,string derivedHash,byte[] derived);
    private static PlayConversion ConvertInput(PlaySnapshotDocument input,DataWorkspace? workspace)
    {
        const string legacy="scene-starter.scene.v1", assetsFormat="scene-starter.scene.v2";
        if(Encoding.UTF8.GetByteCount(input.Text)>1024*1024) return new(legacy,null,"The SceneStarter scene accepts up to one MiB.");
        var scene=SceneRuntimeReader.Read(input.Text).Scene;
        var ids=scene.Objects.Select(item=>item.Asset).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if(ids.Length==0) return new(legacy,Encoding.UTF8.GetBytes(input.Text));
        if(ids.Length>256) return new(assetsFormat,null,"SceneStarter accepts at most 256 pinned assets.");
        var catalog=workspace?.SceneAssets;
        if(catalog is null) return new(assetsFormat,null,"Open this project's admitted asset catalog before Play.");
        var selections=new List<AssetCatalog.Selection>();var pins=new List<Pin>();long total=0;
        foreach(var id in ids)
        {
            var selected=catalog.Select(id);
            if(selected is null || !catalog.TryResolve(selected,out var lease,out _))
                return new(assetsFormat,null,$"Asset {id} is missing, stale or not admitted for Play.");
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
