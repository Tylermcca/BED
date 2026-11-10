using Brutal.Authoring.Editor;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Brutal.Authoring.Editor.ImGui;

/// <summary>Read-only selection and metadata over an existing asset browser; no importing or placement.</summary>
public sealed class AssetsPanel(AssetBrowser browser, Action<AssetEntry>? dragSource = null)
{
    private readonly ImInputString search = new(1024, ""u8);
    private readonly AssetCatalog? catalog;
    private AssetCatalog.Lease? thumbnailLease;
    private AssetThumbnail? thumbnail;
    private string thumbnailWhy = "";
    private AssetCatalog.Lease? modelDetailsLease;
    private int? modelParts;
    private string modelWhy = "";
    private bool problemsOnly;

    // Only the workspace's admitted catalog supplies pixels; browser paths never do.
    internal AssetsPanel(AssetBrowser browser, AssetCatalog catalog, Action<AssetEntry>? dragSource)
        : this(browser, dragSource) => this.catalog = catalog;
    private (string Guid, string Path)? selected;

    /// <summary>The selected current entry, or null if it disappeared on refresh.</summary>
    public AssetEntry? Selected => selected is { } key
        ? browser.Entries.FirstOrDefault(e => e.Guid == key.Guid && e.RelativePath == key.Path) : null;

    /// <summary>The existing panel registration seam; hosts choose when to attach it.</summary>
    public Panel AsPanel() => new("editor.assets", "Assets", Draw, Width: 480, Height: 500);

    /// <summary>Draws searchable records, metadata and a bounded preview of admitted texture content.</summary>
    public void Draw()
    {
        Ui.InputText("Search assets"u8, search);
        Ui.Checkbox("Problems only"u8, ref problemsOnly);
        var entries = browser.Search(search.ToString() ?? "", problemsOnly);
        Ui.TextDisabled($"{entries.Count} of {browser.Entries.Count} assets");
        if (entries.Count == 0) Ui.TextWrapped(browser.Entries.Count == 0 ? "No imported assets." : "No matching assets.");
        if (Ui.BeginChild("Asset list"u8, new float2(0, 160), ImGuiChildFlags.Borders))
        {
            foreach (var entry in entries)
            {
                var key = (entry.Guid, entry.RelativePath);
                if (Ui.Selectable($"{entry.RelativePath}{(entry.HasProblems ? " (needs attention)" : "")}##asset-{entry.Guid}-{entry.RelativePath}", selected == key))
                    selected = key;
                dragSource?.Invoke(entry);
            }
        }
        Ui.EndChild();
        if (Selected is { } current)
        {
            Ui.TextWrapped($"Path: {current.RelativePath}");
            Ui.TextWrapped($"ID: {current.Guid}");
            Ui.TextWrapped($"Importer: {current.Importer}");
            Ui.TextWrapped(FileType(current.RelativePath));
            Ui.TextWrapped($"Licence: {(string.IsNullOrWhiteSpace(current.Licence) ? "Not recorded" : current.Licence)}");
            Ui.TextWrapped($"AI use: {current.AiUse}");
            if (browser.Index.Resolve(current.Guid) is null)
                Ui.TextWrapped("Source file unavailable: it is missing or shares an ID with another asset. Check the problems below."u8);
            else
                Ui.TextWrapped("Source file found. Placement and export need separate checks."u8);
            DrawThumbnail(current);
            foreach (var issue in current.Issues) Ui.TextWrapped($"{issue.Severity}: {issue.Message}");
        }
        else Ui.TextWrapped(selected is null ? "Select an asset to see its details." : "The selected asset is no longer listed. Select another asset.");
        foreach (var issue in browser.General) Ui.TextWrapped($"{issue.Severity}: {issue.Message}");
        if (browser.NotImported.Count > 0)
        {
            Ui.TextWrapped("Not imported"u8);
            foreach (var path in browser.NotImported) Ui.TextWrapped(path);
        }
    }
    private void DrawThumbnail(AssetEntry entry)
    {
        if (catalog is null) return;
        var resolution = catalog.Resolve(entry.Guid);
        var texturePath = Path.GetExtension(entry.RelativePath).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".tga" or ".bedtex";
        var lease = resolution.Current;
        if (lease is null || browser.Index.Resolve(entry.Guid) is null)
        {
            thumbnailLease = null; thumbnail = null;modelDetailsLease=null;modelParts=null;
            if (texturePath) Ui.TextWrapped($"Texture preview unavailable: {(lease is null ? resolution.Reason : "The source file is missing or its ID is shared.")}");
            return;
        }
        if (lease.Texture is null && !texturePath)
        {
            thumbnailLease = null; thumbnail = null;
            if(Path.GetExtension(entry.RelativePath).ToLowerInvariant() is not (".obj" or ".gltf" or ".glb"))return;
            if(!ReferenceEquals(lease,modelDetailsLease))
            {
                modelDetailsLease=lease;modelParts=null;modelWhy="";
                try
                {
                    if(lease.Length>Brutal.Authoring.Runtime.BedMeshReader.MaxBytes)throw new InvalidDataException("The model exceeds the details byte limit.");
                    var bytes=new byte[lease.Length];lease.CopyTo(bytes);
                    modelParts=Brutal.Authoring.Runtime.BedMeshReader.Decode(bytes).Parts.Count;
                }
                catch(InvalidDataException error){modelWhy=error.Message;}
            }
            Ui.TextWrapped(modelParts is {} count?$"Model: {entry.RelativePath}, {count} {(count==1?"part":"parts")}.":$"Model details unavailable: {modelWhy}");
            return;
        }
        modelDetailsLease=null;modelParts=null;
        if (!ReferenceEquals(lease, thumbnailLease))
        {
            thumbnailLease = lease;
            thumbnail = AssetThumbnail.Create(lease, out thumbnailWhy);
        }
        if (thumbnail is null) { Ui.TextWrapped($"Texture preview unavailable: {thumbnailWhy}"); return; }
        var description=$"Texture thumbnail: {entry.RelativePath}, {thumbnail.Width} x {thumbnail.Height} pixels";
        Ui.TextWrapped(description,"asset.thumbnail.description");
        Ui.TextWrapped($"Size: {thumbnail.Width} x {thumbnail.Height} pixels");
        thumbnail.Draw(description);
    }

    private static string FileType(string path)
    {
        var extension = Path.GetExtension(path).ToUpperInvariant();
        return extension switch
        {
            ".OBJ" => "Model (OBJ)",
            ".GLTF" => "Model (glTF)",
            ".GLB" => "Model (GLB)",
            "" => "File type: not specified",
            _ => $"File type: {extension[1..]}"
        };
    }
}

