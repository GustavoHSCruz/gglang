namespace ggLang.Compiler.Analysis;

/// <summary>
/// Severity level for diagnostic messages.
/// </summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Represents a single diagnostic message (error, warning, or info).
/// </summary>
public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Message,
    int Line,
    int Column)
{
    public override string ToString()
    {
        var level = Severity switch
        {
            DiagnosticSeverity.Error => "\u001b[31merror\u001b[0m",
            DiagnosticSeverity.Warning => "\u001b[33mwarning\u001b[0m",
            DiagnosticSeverity.Info => "\u001b[36minfo\u001b[0m",
            _ => "unknown"
        };
        var location = Line > 0 ? $" ({Line}:{Column})" : "";
        return $"  {level}{location}: {Message}";
    }
}

/// <summary>
/// Collects diagnostic messages during compilation phases.
/// </summary>
public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = [];

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;
    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public void ReportError(string message, int line, int column)
    {
        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, message, line, column));
    }

    public void ReportWarning(string message, int line, int column)
    {
        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, message, line, column));
    }

    public void ReportInfo(string message, int line, int column)
    {
        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info, message, line, column));
    }

    /// <summary>
    /// Returns all diagnostics sorted by line number.
    /// </summary>
    public IEnumerable<Diagnostic> GetSorted()
    {
        return _diagnostics.OrderBy(d => d.Line).ThenBy(d => d.Column);
    }

    /// <summary>
    /// Returns only error diagnostics.
    /// </summary>
    public IEnumerable<Diagnostic> GetErrors()
    {
        return _diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
    }

    public void Clear() => _diagnostics.Clear();
}
