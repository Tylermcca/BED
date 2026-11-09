using ImCode.Text;

namespace ImCode.Languages;

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record Diagnostic(TextRange Range, DiagnosticSeverity Severity, string Message);
