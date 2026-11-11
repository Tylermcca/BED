namespace Bed;

/// <summary>
/// One edit to a document of type <typeparamref name="TDoc"/>. Commands are the only supported way to change a document
/// (worksheet D11): a session applies them, validates the result before committing, and records them for undo and replay.
/// A command is a plain record whose properties are its arguments; the <see cref="CommandCatalog{TDoc}"/> gives it a stable
/// id and version and turns it to and from JSON.
/// </summary>
/// <typeparam name="TDoc">The document type. Documents are immutable: a command returns a new one.</typeparam>
public interface ICommand<TDoc> where TDoc : class
{
    /// <summary>Applies the edit. Returns the new document, or the issues that stop it. Must not change <paramref name="document"/>.</summary>
    CommandResult<TDoc> Apply(TDoc document);
}

/// <summary>The outcome of applying a command.</summary>
/// <param name="Document">The new document, or null when the command was refused.</param>
/// <param name="Issues">Problems found (errors refuse the edit; warnings let it through).</param>
/// <param name="Resolved">The command as it should be recorded, when applying it settled something left open, such as a
/// generated id. Recording the resolved form makes replays reproduce the same document.</param>
public sealed record CommandResult<TDoc>(TDoc? Document, IReadOnlyList<Issue> Issues, ICommand<TDoc>? Resolved = null) where TDoc : class
{
    /// <summary>True when the command produced a document and found no errors.</summary>
    public bool Ok => Document is not null && !Issues.HasErrors();

    /// <summary>A successful edit.</summary>
    public static CommandResult<TDoc> Done(TDoc document, ICommand<TDoc>? resolved = null) => new(document, [], resolved);

    /// <summary>A refused edit.</summary>
    public static CommandResult<TDoc> Refused(params Issue[] issues) => new(null, issues);
}

/// <summary>Checks a whole document. A session runs it on every candidate before committing an edit.</summary>
public interface IValidator<TDoc> where TDoc : class
{
    /// <summary>Every problem in <paramref name="document"/>; empty when it is valid.</summary>
    IReadOnlyList<Issue> Validate(TDoc document);
}

/// <summary>Reads and writes one document type as canonical text: the same document always writes the same bytes.</summary>
public interface IDocumentFormat<TDoc> where TDoc : class
{
    /// <summary>The document kind written in the header (for example <c>toy.board</c>).</summary>
    string Kind { get; }

    /// <summary>The format version this code writes. Other versions are refused unless a migration is added.</summary>
    int Version { get; }

    /// <summary>Reads a document. Throws <see cref="DocumentFormatException"/> naming the JSON path of the first problem.</summary>
    TDoc Read(string text);

    /// <summary>Writes the canonical text: UTF-8, LF line endings, a final newline, fixed property order.</summary>
    string Write(TDoc document);
}

/// <summary>A document or journal that can't be read, with the JSON path of the problem.</summary>
public sealed class DocumentFormatException(string path, string message) : Exception($"{path}: {message}")
{
    /// <summary>The JSON path of the problem, for example <c>$.items[2].x</c>.</summary>
    public string Path { get; } = path;
}
