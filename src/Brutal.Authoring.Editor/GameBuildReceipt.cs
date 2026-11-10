using System.Collections.Immutable;
namespace Brutal.Authoring.Editor;

/// <summary>One bounded plain build log line, not a Play protocol message.</summary>
public sealed record GameBuildLog(bool Error,string Text);
/// <summary>Terminal owned build result. Success means exact staged bytes were checked, never published.
/// ManifestHash is empty when a post-build inventory could not be pinned.</summary>
public sealed record GameBuildReceipt(string SavedRevision,string ManifestHash,string OutputDirectory,int? ExitCode,bool Succeeded,string Reason,ImmutableArray<ExportStagedOutput> Outputs);
