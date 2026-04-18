using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Provider;

internal sealed record TerraformValidateResult(IReadOnlyList<TerraformDiagnostic>? Diagnostics = null)
{
    public static TerraformValidateResult Empty { get; } = new([]);
}

internal sealed record TerraformConfigureResult(
    object? ProviderState = null,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);

internal sealed record TerraformReadResult(
    TerraformDynamicValue NewState,
    byte[]? PrivateState = null,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);

internal sealed record TerraformPlanResult(
    TerraformDynamicValue PlannedState,
    byte[]? PlannedPrivateState = null,
    IReadOnlyList<TerraformAttributePath>? RequiresReplace = null,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);

internal sealed record TerraformApplyResult(
    TerraformDynamicValue NewState,
    byte[]? PrivateState = null,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);

internal sealed record TerraformImportResource(
    TerraformDynamicValue State,
    byte[]? PrivateState = null);

internal sealed record TerraformImportResult(
    IReadOnlyList<TerraformImportResource> Resources,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);
