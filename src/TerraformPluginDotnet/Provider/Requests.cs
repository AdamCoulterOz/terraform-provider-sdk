using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Provider;

internal sealed record TerraformProviderValidateRequest(TerraformDynamicValue Config);

internal sealed record TerraformProviderConfigureRequest(
    TerraformDynamicValue Config,
    string TerraformVersion,
    bool DeferralAllowed);

internal sealed record TerraformResourceValidateRequest(TerraformDynamicValue Config);

internal sealed record TerraformDataSourceValidateRequest(TerraformDynamicValue Config);

internal sealed record TerraformResourceReadRequest(
    TerraformDynamicValue CurrentState,
    byte[] PrivateState,
    object? ProviderState);

internal sealed record TerraformResourcePlanRequest(
    TerraformDynamicValue PriorState,
    TerraformDynamicValue ProposedNewState,
    TerraformDynamicValue Config,
    byte[] PriorPrivateState,
    object? ProviderState);

internal sealed record TerraformResourceApplyRequest(
    TerraformDynamicValue PriorState,
    TerraformDynamicValue PlannedState,
    TerraformDynamicValue Config,
    byte[] PlannedPrivateState,
    object? ProviderState);

internal sealed record TerraformDataSourceReadRequest(
    TerraformDynamicValue Config,
    object? ProviderState);

internal sealed record TerraformResourceImportRequest(
    string TypeName,
    string Id,
    object? ProviderState);
