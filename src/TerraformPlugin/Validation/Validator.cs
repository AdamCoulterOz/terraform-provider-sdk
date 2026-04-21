using System.ComponentModel.DataAnnotations;
using System.Reflection;
using TerraformPlugin.Diagnostics;
using TerraformPlugin.Schema;
using TerraformPlugin.Types;

namespace TerraformPlugin.Validation;

public static class Validator
{
    public static IReadOnlyList<Diagnostic> Validate(object model, object? providerState = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var context = new ValidationContext(
            model,
            serviceProvider: null,
            items: providerState is null
                ? null
                : new Dictionary<object, object?> { [ValidationKeys.ProviderState] = providerState });

        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(model, context, results, validateAllProperties: true);

        return results.SelectMany(result => ToDiagnostics(model.GetType(), result)).ToArray();
    }

    private static IEnumerable<Diagnostic> ToDiagnostics(Type modelType, System.ComponentModel.DataAnnotations.ValidationResult result)
    {
        var summary = result is ValidationResult terraformResult
            ? terraformResult.Summary
            : result.ErrorMessage ?? "Validation failed";
        var detail = result is ValidationResult custom
            ? custom.Detail
            : result.ErrorMessage ?? "Validation failed.";
        var memberNames = result.MemberNames?.ToArray() ?? [];

        if (memberNames.Length == 0)
        {
            return [Diagnostic.Error(summary, detail)];
        }

        return memberNames.Select(memberName =>
        {
            var attributePath = TryGetAttributePath(modelType, memberName, out var path) ? path : null;
            return Diagnostic.Error(summary, detail, attributePath);
        });
    }

    private static bool TryGetAttributePath(Type modelType, string memberName, out AttributePath path)
    {
        var member = modelType.GetMember(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(static candidate => candidate.MemberType is MemberTypes.Property or MemberTypes.Field);

        if (member is not null)
        {
            path = AttributePath.Root(ModelConventions.GetSchemaMemberName(member));
            return true;
        }

        path = default!;
        return false;
    }
}
