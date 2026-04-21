namespace TerraformPlugin.Validation;

public sealed class ValidationResult(string summary, string detail, IEnumerable<string>? memberNames = null)
    : System.ComponentModel.DataAnnotations.ValidationResult(detail, memberNames)
{
    public string Summary { get; } = summary;
    public string Detail { get; } = detail;
}
