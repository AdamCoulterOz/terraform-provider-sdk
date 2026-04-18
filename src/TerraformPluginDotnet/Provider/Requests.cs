using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Provider;

public sealed record TerraformProviderValidateRequest(TerraformValue Config);

public sealed record TerraformProviderConfigureRequest(
    TerraformValue Config,
    string TerraformVersion,
    bool DeferralAllowed);

public sealed record TerraformResourceValidateRequest(TerraformValue Config);

public sealed record TerraformDataSourceValidateRequest(TerraformValue Config);

public sealed record TerraformResourceReadRequest(
    TerraformValue CurrentState,
    byte[] PrivateState,
    object? ProviderState);

public sealed record TerraformResourcePlanRequest(
    TerraformValue PriorState,
    TerraformValue ProposedNewState,
    TerraformValue Config,
    byte[] PriorPrivateState,
    object? ProviderState);

public sealed record TerraformResourceApplyRequest(
    TerraformValue PriorState,
    TerraformValue PlannedState,
    TerraformValue Config,
    byte[] PlannedPrivateState,
    object? ProviderState);

public sealed record TerraformDataSourceReadRequest(
    TerraformValue Config,
    object? ProviderState);

public sealed record TerraformResourceImportRequest(
    string TypeName,
    string Id,
    object? ProviderState);
