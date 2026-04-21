using TerraformPlugin.Types;

namespace TerraformPlugin.Diagnostics;

public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Summary,
    string Detail,
    AttributePath? Attribute = null)
{
    public static Diagnostic Error(string summary, string detail, AttributePath? attribute = null) =>
        new(DiagnosticSeverity.Error, summary, detail, attribute);

    public static Diagnostic Warning(string summary, string detail, AttributePath? attribute = null) =>
        new(DiagnosticSeverity.Warning, summary, detail, attribute);
}
