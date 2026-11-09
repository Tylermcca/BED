namespace Brutal.Authoring.Editor.ImGui;

/// <summary>A display label and semicolon-separated wildcard patterns for a file dialog.</summary>
public sealed record FileDialogFilter(string Name, string Pattern);

/// <summary>
/// Host-owned path selection. A result selects a path; it does not open, create or save
/// project data. Null means user cancellation, not an implementation failure. Hosts
/// without native dialogs leave this service absent so the existing browser is used.
/// Calls are synchronous and must follow the host implementation's threading contract.
/// </summary>
public interface IFileDialogs
{
    /// <summary>Choose an existing folder, starting at the optional folder.</summary>
    string? PickFolder(string title, string? start);
    /// <summary>Choose an existing file matching one of the filters.</summary>
    string? OpenFile(string title, IReadOnlyList<FileDialogFilter> filters, string? start);
    /// <summary>Choose a destination name; this method never writes the file.</summary>
    string? SaveFile(string title, IReadOnlyList<FileDialogFilter> filters, string? suggestedName);
}
