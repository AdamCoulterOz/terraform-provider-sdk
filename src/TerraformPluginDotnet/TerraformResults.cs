using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet;

public sealed record TerraformProviderContext(
    string TerraformVersion,
    bool DeferralAllowed);

public sealed record TerraformResourceContext<TProviderState>(
    TProviderState ProviderState,
    byte[]? PrivateState = null,
    byte[]? PriorPrivateState = null,
    byte[]? PlannedPrivateState = null);

public sealed record TerraformDataSourceContext<TProviderState>(TProviderState ProviderState);

public sealed record TerraformModelResult<TModel>(
    TModel? Model,
    byte[]? PrivateState = null,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);

public sealed record TerraformPlanResult<TModel>(
    TModel? PlannedState,
    byte[]? PlannedPrivateState = null,
    IReadOnlyList<TerraformAttributePath>? RequiresReplace = null,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);

public sealed record TerraformImportResult<TModel>(
    IReadOnlyList<TModel> Resources,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);
