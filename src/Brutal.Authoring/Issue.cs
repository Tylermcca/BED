namespace Brutal.Authoring;

/// <summary>How serious an <see cref="Issue"/> is. Only <see cref="Error"/> stops an edit or a load.</summary>
public enum Severity
{
    /// <summary>Worth knowing; nothing is wrong.</summary>
    Info,

    /// <summary>Probably wrong, but the edit goes ahead.</summary>
    Warning,

    /// <summary>Wrong: the edit is refused.</summary>
    Error,
}

/// <summary>A problem found in a document or a command: what went wrong, on which object, at which field. Errors name
/// the object so a host can select it (goal 1: "every error message says what went wrong, on which object").</summary>
/// <param name="Severity">How serious it is.</param>
/// <param name="Message">What went wrong and, where it helps, what to do next.</param>
/// <param name="ObjectId">The object it concerns, if any.</param>
/// <param name="Path">The field or JSON path it concerns, if any (for example <c>$.items[2].label</c>).</param>
public sealed record Issue(Severity Severity, string Message, string? ObjectId = null, string? Path = null)
{
    /// <summary>An error.</summary>
    public static Issue Error(string message, string? objectId = null, string? path = null) => new(Severity.Error, message, objectId, path);

    /// <summary>A warning.</summary>
    public static Issue Warning(string message, string? objectId = null, string? path = null) => new(Severity.Warning, message, objectId, path);

    /// <inheritdoc />
    public override string ToString()
    {
        var where = (ObjectId, Path) switch
        {
            (not null, not null) => $" [{ObjectId} {Path}]",
            (not null, null) => $" [{ObjectId}]",
            (null, not null) => $" [{Path}]",
            _ => "",
        };
        return $"{Severity.ToString().ToLowerInvariant()}{where}: {Message}";
    }
}

/// <summary>Helpers over issue lists.</summary>
public static class Issues
{
    /// <summary>True when any issue is an error.</summary>
    public static bool HasErrors(this IEnumerable<Issue> issues) => issues.Any(i => i.Severity == Severity.Error);
}
