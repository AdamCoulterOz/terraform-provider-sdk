using TerraformPlugin.Types;

namespace TerraformPlugin.Provider;

internal sealed record ProviderValidateRequest(DynamicValue Config);

internal sealed record ProviderConfigureRequest(
    DynamicValue Config,
    string TerraformVersion,
    bool DeferralAllowed);

internal sealed record ValidateRequest(DynamicValue Config);

internal sealed record DataSourceValidateRequest(DynamicValue Config);

internal sealed record ListResourceValidateRequest(
    DynamicValue Config,
    DynamicValue IncludeResourceObject,
    DynamicValue Limit);

internal sealed record ResourceReadRequest(
    DynamicValue CurrentState,
    byte[] PrivateState,
    object? ProviderState);

internal sealed record ResourcePlanRequest(
    DynamicValue PriorState,
    DynamicValue ProposedNewState,
    DynamicValue Config,
    byte[] PriorPrivateState,
    object? ProviderState);

internal sealed record ResourceApplyRequest(
    DynamicValue PriorState,
    DynamicValue PlannedState,
    DynamicValue Config,
    byte[] PlannedPrivateState,
    object? ProviderState);

internal sealed record DataSourceReadRequest(
    DynamicValue Config,
    object? ProviderState);

internal sealed record ListResourceRequest(
    DynamicValue Config,
    bool IncludeResourceObject,
    long Limit,
    object? ProviderState);

internal sealed record ResourceImportRequest(
    string TypeName,
    string Id,
    object? ProviderState);
