using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Provider;

public sealed record TerraformValidateResult(IReadOnlyList<TerraformDiagnostic>? Diagnostics = null)
{
    public static TerraformValidateResult Empty { get; } = new([]);
}

public sealed record TerraformConfigureResult(
    object? ProviderState = null,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);

public sealed record TerraformReadResult(
    TerraformValue NewState,
    byte[]? PrivateState = null,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);

public sealed record TerraformPlanResult(
    TerraformValue PlannedState,
    byte[]? PlannedPrivateState = null,
    IReadOnlyList<TerraformAttributePath>? RequiresReplace = null,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);

public sealed record TerraformApplyResult(
    TerraformValue NewState,
    byte[]? PrivateState = null,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);

public sealed record TerraformImportResource(
    string TypeName,
    TerraformValue State,
    byte[]? PrivateState = null);

public sealed record TerraformImportResult(
    IReadOnlyList<TerraformImportResource> Resources,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);
