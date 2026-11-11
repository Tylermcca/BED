namespace Bed.Editor.ImGui;
/// <summary>App-owned verification bridge; no UI build receipt alone grants copying.</summary>
public interface IGameExportHost:IGameBuildHost
{
    /// <summary>Opens only an owner-issued log folder after an explicit user action.</summary>
    bool OpenLogFolder(string path,out string reason){reason="This host cannot open the export log folder.";return false;}
    /// <summary>Runs actual bounded smoke for this exact output inventory.</summary>
    Task<GameExportSmokeResult> Smoke(GameExportPlan plan,GameBuildReceipt receipt,GameContentManifest outputs,GameLaunchSpec smoke,CancellationToken cancellation);
}
/// <summary>Copy authority returned only by actual App verification.</summary>
public interface IVerifiedGameBuild
{
    /// <summary>Owned verified output directory.</summary>
    string OutputDirectory {get;}
    /// <summary>Rechecks and copies only into a new or empty destination; cancellation leaves no partial destination.</summary>
    bool CopyTo(string destination,out string reason,CancellationToken cancellation=default);
}
/// <summary>Verification outcome; a missing build keeps copying unavailable.</summary>
public sealed record GameExportSmokeResult(IVerifiedGameBuild? Build,string Reason);
