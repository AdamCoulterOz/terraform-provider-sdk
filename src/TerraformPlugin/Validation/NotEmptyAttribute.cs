using System.ComponentModel.DataAnnotations;

namespace TerraformPlugin.Validation;

public sealed class NotEmptyAttribute : ValidationAttribute
{
    protected override System.ComponentModel.DataAnnotations.ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (!ValidationUtilities.TryGetKnownValue<string>(value, out var text) || !string.IsNullOrWhiteSpace(text))
        {
            return System.ComponentModel.DataAnnotations.ValidationResult.Success;
        }

        var memberName = ValidationUtilities.GetSchemaMemberName(validationContext);
        var summary = $"Invalid {memberName}";
        var detail = string.IsNullOrWhiteSpace(ErrorMessage)
            ? $"{memberName} must not be empty."
            : ErrorMessage!;

        return new ValidationResult(summary, detail, MemberNames(validationContext));
    }

    private static IEnumerable<string> MemberNames(ValidationContext validationContext) =>
        string.IsNullOrWhiteSpace(validationContext.MemberName)
            ? []
            : [validationContext.MemberName];
}
