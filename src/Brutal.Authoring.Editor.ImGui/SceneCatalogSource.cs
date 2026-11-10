using System.Numerics;
using Brutal.Authoring.Editor;

namespace Brutal.Authoring.Editor.ImGui;

// One workspace admission supplies every mesh. Source files and browser paths are never fallback geometry.
internal sealed class SceneCatalogSource(Action changed) : IViewportSource<Scene>, IViewportTransformSource<Scene>
{
    private readonly Dictionary<string, (AssetCatalog.Lease? Lease, MeshData? Mesh)> meshes = new(StringComparer.Ordinal);
    internal SceneEditorFlags? Flags;
    private AssetCatalog? catalog;
    private SceneViewport? source;
    private SceneViewport Source => source ??= new(Resolve);
    internal AssetCatalog? Catalog
    {
        get => catalog;
        set { if (ReferenceEquals(catalog, value)) return; catalog = value; meshes.Clear(); changed(); }
    }
    private MeshData? Resolve(string id)
    {
        var lease = catalog?.Resolve(id).Current;
        if (meshes.TryGetValue(id, out var cached) && ReferenceEquals(cached.Lease, lease)) return cached.Mesh;
        MeshData? mesh = null;
        if (lease is not null && catalog?.Select(id) is { } selection)
        {
            var preview = new ModelPreview(catalog); preview.Select(selection);
            preview.TryMesh(out mesh, out _);
        }
        meshes[id] = (lease, mesh); changed(); return mesh;
    }
    public IEnumerable<ViewportItem> Items(Scene scene) => Source.Items(scene).Where(item=>Flags?.Hidden(scene,item.Id)!=true);
    public ICommand<Scene>? MoveTo(Scene scene, string id, Vector3 world) => Source.MoveTo(scene, id, world);
    public ViewportTransformPlan<Scene> Rotate(Scene scene, IReadOnlyList<string> selected, Vector3 pivot, Quaternion basis,
        Vector3 axis, float radians, float snapStep) => Source.Rotate(scene, selected, pivot, basis, axis, radians, snapStep);
    public ViewportTransformPlan<Scene> Scale(Scene scene, IReadOnlyList<string> selected, Vector3 factors) => Source.Scale(scene, selected, factors);
}
