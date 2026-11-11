using System.Collections.Immutable;
using System.Text.Json;
namespace Bed;

// Pure saved-scene operations. Runtime preflight forbids authoring content before the resolver can see it.
internal static class Scene2Runtime
{
    internal static Scene2 Flatten(Scene2 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var resolved=Scene2Resolver.Resolve(snapshot);
        return new(snapshot.Space,resolved.Objects,[],[],resolved.Objects.Select(o=>o.Object.Id).ToImmutableArray()){Physics=snapshot.Physics};
    }

    internal static (Scene2 Scene,bool AssetsChecked,IReadOnlyList<Issue> AssetIssues) Read(string text,Func<string,bool>? assetExists=null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var root=Scene2Read.Parse(text,32*1024*1024);
        if(root.ValueKind==JsonValueKind.Object)
            foreach(var field in new[]{"bases","instances"})
                if(root.TryGetProperty(field,out var content)&&content.ValueKind==JsonValueKind.Array&&content.GetArrayLength()!=0)
                    throw new DocumentFormatException("$/"+field,"Build this authoring scene before running it; the runtime accepts only flattened scene version2 without prefab bases or instances.");
        var scene=Flatten(new Scene2Read(root,"").Scene());
        var assets=assetExists is null?Array.Empty<Issue>():SceneAssets.Check(new Scene(scene.Objects.Select(o=>o.Object).ToImmutableArray()),assetExists).ToArray();
        return(scene,assetExists is not null,Array.AsReadOnly(assets));
    }
}
