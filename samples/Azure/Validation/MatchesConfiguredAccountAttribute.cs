using System.ComponentModel.DataAnnotations;
using TerraformPlugin.Validation;

namespace Azure.Validation;

internal sealed class MatchesConfiguredAccountAttribute : ValidationAttribute
{
    protected override System.ComponentModel.DataAnnotations.ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (!ValidationUtilities.TryGetKnownValue<string>(value, out var accountName) ||
            !ValidationUtilities.TryGetProviderState<ProviderState>(validationContext, out var providerState) ||
            string.Equals(accountName, providerState.AccountName, StringComparison.Ordinal))
            return System.ComponentModel.DataAnnotations.ValidationResult.Success;

        return new TerraformPlugin.Validation.ValidationResult(
                "Configured account mismatch",
                $"This sample provider is connected to storage account '{providerState.AccountName}', but the resource targeted '{accountName}'.",
                MemberNames(validationContext));
    }

    private static IEnumerable<string> MemberNames(ValidationContext validationContext) =>
        string.IsNullOrWhiteSpace(validationContext.MemberName)
            ? []
            : [validationContext.MemberName];
}
