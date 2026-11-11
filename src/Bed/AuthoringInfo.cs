namespace Bed;

/// <summary>The shared authoring library's identity. The plan it follows is
/// docs/plan/worksheet_r2.md.</summary>
public static class AuthoringInfo
{
    /// <summary>The worksheet revision this code follows.</summary>
    public const string PlanRevision = "worksheet_r2";

    /// <summary>The public API's version, which plug-ins name as their minimum (<see cref="PluginManifest.Api"/>). Before
    /// 1.0, a minor version may change the API, so a plug-in must match the minor version; from 1.0, only the major.</summary>
    public static readonly ApiVersion ApiVersion = new(0, 2, 0);
}
