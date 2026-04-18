using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Diagnostics;

public sealed record TerraformDiagnostic(
    TerraformDiagnosticSeverity Severity,
    string Summary,
    string Detail,
    TerraformAttributePath? Attribute = null)
{
    public static TerraformDiagnostic Error(string summary, string detail, TerraformAttributePath? attribute = null) =>
        new(TerraformDiagnosticSeverity.Error, summary, detail, attribute);

    public static TerraformDiagnostic Warning(string summary, string detail, TerraformAttributePath? attribute = null) =>
        new(TerraformDiagnosticSeverity.Warning, summary, detail, attribute);
}
