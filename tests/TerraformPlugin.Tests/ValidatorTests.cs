using System.ComponentModel.DataAnnotations;
using TerraformPlugin.Diagnostics;
using TerraformPlugin.Schema;
using TerraformPlugin.Types;
using TerraformPlugin.Validation;

namespace TerraformPlugin.Tests;

public sealed class ValidatorTests
{
    [Fact]
    public void Validate_ReportsKnownEmptyWrappedString()
    {
        var model = new NotEmptyModel
        {
            Name = TF<string>.Known(" "),
        };

        var diagnostics = Validation.Validator.Validate(model);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("Invalid name", diagnostic.Summary);
        Assert.Equal("name must not be empty.", diagnostic.Detail);
        Assert.Equal(AttributePath.Root("name"), diagnostic.Attribute);
    }

    [Fact]
    public void Validate_IgnoresUnknownWrappedString()
    {
        var model = new NotEmptyModel
        {
            Name = TF<string>.Unknown(),
        };

        var diagnostics = Validation.Validator.Validate(model);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Validate_PassesProviderStateToCustomAttributes()
    {
        var model = new ProviderScopedModel
        {
            Name = TF<string>.Known("other"),
        };

        var diagnostics = Validation.Validator.Validate(model, new ExpectedNameProviderState("expected"));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("Name mismatch", diagnostic.Summary);
        Assert.Equal(AttributePath.Root("name"), diagnostic.Attribute);
    }

    private sealed class NotEmptyModel
    {
        [TFAttribute]
        [NotEmpty]
        public TF<string> Name { get; init; }
    }

    private sealed class ProviderScopedModel
    {
        [TFAttribute]
        [MatchesExpectedName]
        public TF<string> Name { get; init; }
    }

    private sealed record ExpectedNameProviderState(string ExpectedName);

    private sealed class MatchesExpectedNameAttribute : ValidationAttribute
    {
        protected override System.ComponentModel.DataAnnotations.ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (!ValidationUtilities.TryGetKnownValue<string>(value, out var text) ||
                !ValidationUtilities.TryGetProviderState<ExpectedNameProviderState>(validationContext, out var providerState) ||
                string.Equals(text, providerState.ExpectedName, StringComparison.Ordinal))
            {
                return System.ComponentModel.DataAnnotations.ValidationResult.Success;
            }

            return new Validation.ValidationResult(
                "Name mismatch",
                "provider state name did not match.",
                string.IsNullOrWhiteSpace(validationContext.MemberName) ? [] : [validationContext.MemberName]);
        }
    }
}
