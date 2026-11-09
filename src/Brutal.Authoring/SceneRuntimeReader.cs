namespace Brutal.Authoring;

/// <summary>A structurally valid scene and optional asset-availability diagnostics. Missing assets remain placeholders.</summary>
/// <param name="Scene">The complete immutable scene in source object order, with local transforms unchanged.</param>
/// <param name="AssetsChecked">Whether an asset predicate was supplied; false never means all assets exist.</param>
/// <param name="AssetIssues">The existing SceneAssets findings, in object order. These do not discard the scene.</param>
public sealed record SceneRuntimeReadResult(Scene Scene, bool AssetsChecked, IReadOnlyList<Issue> AssetIssues);

/// <summary>Pure-core text reader. It creates no editor/session, reads no files and instantiates no runtime objects.</summary>
public static class SceneRuntimeReader
{
    /// <summary>Reads the existing version-one scene format and validates identities and links without rewriting local TRS.
    /// A missing asset retains its object and reference; a consumer decides whether placeholders are sufficient to run.
    /// No asset cache or filesystem policy is introduced.</summary>
    /// <param name="text">Already decoded scene JSON. The caller owns input acquisition.</param>
    /// <param name="kind">The explicitly expected format kind; it is never inferred from untrusted input.</param>
    /// <param name="assetExists">Optional availability lookup, called by SceneAssets in object order. Exceptions propagate.</param>
    /// <exception cref="DocumentFormatException">Syntax, kind, version or shape is invalid; the original path and message survive.</exception>
    /// <exception cref="InvalidDocumentException">Identity or graph validation fails; all original ordered issues survive.</exception>
    public static SceneRuntimeReadResult Read(string text, string kind = "scene", Func<string, bool>? assetExists = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(kind);
        var scene = new SceneFormat(kind).Read(text);
        var issues = new SceneValidator().Validate(scene);
        if (issues.HasErrors()) throw new InvalidDocumentException(issues);
        var assets = assetExists is null ? Array.Empty<Issue>() : SceneAssets.Check(scene, assetExists).ToArray();
        return new(scene, assetExists is not null, Array.AsReadOnly(assets));
    }
}
