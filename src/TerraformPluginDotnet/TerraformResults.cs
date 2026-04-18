using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet;

public sealed record TerraformProviderContext(
    string TerraformVersion,
    bool DeferralAllowed);

public sealed record TerraformResourceContext<TProviderState>(TProviderState ProviderState);

public sealed record TerraformDataSourceContext<TProviderState>(TProviderState ProviderState);

public sealed record TerraformModelResult<TModel>(
    TModel? Model,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);

public sealed record TerraformPlanResult<TModel>(
    TModel? PlannedState,
    IReadOnlyList<TerraformAttributePath>? RequiresReplace = null,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);

public sealed record TerraformImportResult<TModel>(
    IReadOnlyList<TModel> Resources,
    IReadOnlyList<TerraformDiagnostic>? Diagnostics = null);
