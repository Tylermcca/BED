using System.Collections.Immutable;
using Bed.Editor;
namespace Bed.Editor.ImGui;

/// <summary>App-owned supervised build bridge. The UI supplies admitted Saved input and never references App/native process types.</summary>
public interface IGameBuildHost
{
    /// <summary>Starts only after the real dirty-state/save decision; no source or output declaration is inferred.</summary>
    IGameBuildOperation Start(GameBuildSpec spec,string projectRoot,bool hasUnsavedChanges,GameExportPlan plan);
}
/// <summary>One owned build attempt. Successful Completion means inventory/staging, not smoke or permission to copy.</summary>
public interface IGameBuildOperation:IDisposable
{
    /// <summary>Owner-local full export log, outside the game output inventory, when supplied by the host.</summary>
    string? LogPath=>null;
    /// <summary>Terminal observed owned completion; never a UI-local success flag.</summary>
    Task<GameBuildReceipt> Completion {get;}
    /// <summary>Pinned built manifest, available only after successful inventory; used by the subsequent game-owned smoke.</summary>
    GameContentManifest? OutputManifest {get;}
    /// <summary>Drains bounded plain process logs.</summary>
    ImmutableArray<GameBuildLog> DrainLogs();
    /// <summary>Cancels only this attempt and its owned resources.</summary>
    void Cancel();
}
