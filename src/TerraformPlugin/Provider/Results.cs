using TerraformPlugin.Diagnostics;
using TerraformPlugin.Types;

namespace TerraformPlugin.Provider;

internal sealed record ValidateResult(IReadOnlyList<Diagnostic>? Diagnostics = null)
{
    public static ValidateResult Empty { get; } = new([]);
}

internal sealed record ConfigureResult(
    object? ProviderState = null,
    IReadOnlyList<Diagnostic>? Diagnostics = null);

internal sealed record ReadResult(
    DynamicValue NewState,
    byte[]? PrivateState = null,
    IReadOnlyList<Diagnostic>? Diagnostics = null);

internal sealed record PlanResult(
    DynamicValue PlannedState,
    byte[]? PlannedPrivateState = null,
    IReadOnlyList<AttributePath>? RequiresReplace = null,
    IReadOnlyList<Diagnostic>? Diagnostics = null);

internal sealed record ApplyResult(
    DynamicValue NewState,
    byte[]? PrivateState = null,
    IReadOnlyList<Diagnostic>? Diagnostics = null);

internal sealed record ImportResource(
    DynamicValue State,
    byte[]? PrivateState = null);

internal sealed record ImportResult(
    IReadOnlyList<ImportResource> Resources,
    IReadOnlyList<Diagnostic>? Diagnostics = null);
