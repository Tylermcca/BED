namespace Brutal.Authoring.Editor.ImGui;

/// <summary>App-owned process bridge used by a declared adapter. UI/model code never references the App assembly
/// or discovers an executable. Start is called only after the user resolves the saved-files decision.</summary>
public interface ILinkedPlayHost
{
    /// <summary>Starts the declared spec with one immutable Saved snapshot; returns the concrete existing preview bridge.</summary>
    LinkedPlayPreview Start(GameLaunchSpec spec,PlaySnapshot snapshot);
}
