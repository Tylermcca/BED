using Bed.Data;
using Bed.Editor;
using Brutal.ImGuiApi;

namespace Bed.Editor.ImGui;
using ImGui=Brutal.ImGuiApi.ImGui;

public sealed partial class DataWorkspace
{
    private sealed class SceneAssetsBinding
    {
        public required Action Draw;
        public required Func<bool> Begin;
        public required Func<string?> Unavailable;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EditorModel<Scene>, SceneAssetsBinding> sceneAssetBindings = new();
    private ProjectAssetCatalog? ownedProjectAssets;
    internal void BindProjectAssets(ProjectAssetCatalog assets)
    {
        if (!BindSceneAssets(assets.Browser, assets.Catalog)) { assets.Dispose(); return; }
        ownedProjectAssets?.Dispose();
        ownedProjectAssets = assets;
        sceneAssetPicker!.AudioPreview = assets.AudioPreview;
    }
    // Called when the project closes or another replaces it: its assets, and the Gum screen picture (Gum, its watcher).
    internal void ReleaseProjectAssets() { ownedProjectAssets?.Dispose(); ownedProjectAssets = null; ReleaseGum(); }
    private AssetCatalog? sceneAssets;
    /// <summary>The host-admitted scene catalog, or null. Reading it grants no source or distribution permission.</summary>
    public AssetCatalog? SceneAssets => sceneAssets;
    private AssetsPanel? sceneAssetPicker;
    private AssetPlacement? scenePlacement;

    /// <summary>Connects the host's browser and admitted catalog. Browser paths never grant placement authority.</summary>
    public bool BindSceneAssets(AssetBrowser browser, AssetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!Documents.BindAssetCatalog(catalog, out var why)) { Said = why; return false; }
        sceneAssets = catalog;
        scenePlacement?.Cancel();
        scenePlacement = new AssetPlacement(this, catalog);
        sceneAssetPicker = new AssetsPanel(browser, catalog, scenePlacement.DrawSource);
        return true;
    }

    private void AttachSceneAssets(string name, SceneDocumentEntry entry)
    {
        const string panelId = "scene.assets";
        long? revision = null;
        (string Guid, string Path)? selectedKey = null;
        AssetCatalog.Selection? selected = null;
        System.Numerics.Vector3 position = default;
        string? parent = null;
        string? Unavailable()
        {
            if (sceneAssets is null) return "No assets for this project";
            if (SceneWindow?.Editor != entry.Editor) return "Open this scene before adding an object";
            if (entry.Editor.Primary is null && entry.Editor.FocusedOn is not null) return "Step out of focus before placing a root object";
            return Project.ReadOnlyReason(name);
        }
        bool Begin()
        {
            if (Unavailable() is { } why) { Said = why; return false; }
            revision = entry.Session.Revision;
            selectedKey = null;
            selected = null;
            scenePlacement?.Cancel();
            parent = entry.Editor.Primary;
            position = parent is null ? SceneViewport!.Camera.Target : System.Numerics.Vector3.Zero;
            return true;
        }
        void Draw()
        {
            if (sceneAssetPicker is null) { Ui.TextWrapped("No assets for this project"u8); return; }
            var browserHeight=MathF.Max(ImGui.GetFrameHeight()*3,ImGui.GetContentRegionAvail().Y-ImGui.GetFrameHeightWithSpacing()*2-ImGui.GetTextLineHeightWithSpacing()*2);
            if(Ui.BeginChild("Scene asset browser###scene.assets.browser"u8,new Brutal.Numerics.float2(0,browserHeight),ImGuiChildFlags.NavFlattened))sceneAssetPicker.Draw();
            Ui.EndChild();
            foreach (var problem in ownedProjectAssets?.Problems ?? []) Ui.TextWrapped(problem);
            foreach (var warning in ownedProjectAssets?.Warnings ?? []) Ui.TextWrapped(warning);
            var currentKey = sceneAssetPicker.Selected is { } item ? (item.Guid, item.RelativePath) : ((string, string)?)null;
            if (currentKey != selectedKey)
            {
                selectedKey = currentKey;
                selected = currentKey is { } key ? sceneAssets!.Select(key.Item1) : null;
                if (selected is not null && revision == entry.Session.Revision)
                    scenePlacement!.Begin(selected, position, parent);
                else scenePlacement?.Cancel();
            }
            var why = Unavailable();
            if (revision is null) why ??= "Choose Scene > Add object or press Insert to begin placement";
            if (revision != entry.Session.Revision) why ??= "The scene changed; start Add object again";
            if (sceneAssetPicker.Selected is null) why ??= "Select an asset first";
            Ui.BeginDisabled(why is not null, why);
            if (Ui.Button("Add selected object"u8))
            {
                if (selected is null) Said = "The selected asset isn't in this project's assets.";
                else
                {
                    if (scenePlacement!.Commit())
                    {
                        revision = null;
                        entry.Editor.SetOpen(panelId, false);
                    }
                }
            }
            Ui.EndDisabled();
            if (Ui.Button("Cancel placement"u8))
            {
                scenePlacement?.Cancel();
                revision = null;
                entry.Editor.SetOpen(panelId, false);
            }
            if (why is not null) Ui.TextWrapped(why);
            if (Said.Length > 0) Ui.TextWrapped(Said);
        }
        var binding = sceneAssetBindings.GetValue(entry.Editor, _ => new SceneAssetsBinding { Draw = Draw, Begin = Begin, Unavailable = Unavailable });
        binding.Draw = Draw;
        binding.Begin = Begin;
        binding.Unavailable = Unavailable;
        if (!entry.Editor.Panels.Any(p => p.Id == panelId))
            entry.Editor.Add(new Panel(panelId, "Add scene object", () => binding.Draw(), OpenAtStart: false, Width: 480, Height: 600,
                Commands: [new PanelCommand("add", "Add object.", () => binding.Begin(), () => binding.Unavailable())]));
        shortcuts.Default(EditorWindow<Scene>.PanelCommandAction + panelId + "/add", "Insert");
    }
}
