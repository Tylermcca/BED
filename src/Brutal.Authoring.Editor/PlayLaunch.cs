namespace Brutal.Authoring.Editor;

/// <summary>Resolved saved-files launch shared by project settings and the one supervised Play host.
/// Resolution alone does not approve external programs or builds; the caller must enforce the user-local command approval.</summary>
/// <param name="Run">Exact executable, working folder and literal arguments for Play.</param>
/// <param name="Build">Optional exact build command; never implicitly run by settings resolution.</param>
/// <param name="ProjectRoot">Absolute admitted project root supplied to the process environment.</param>
/// <param name="BuildInputs">Project-relative freshness input globs.</param>
public sealed record PlayLaunch(GameLaunchSpec Run, GameLaunchSpec? Build, string ProjectRoot, IReadOnlyList<string> BuildInputs);
