using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Provider;
using TerraformPluginDotnet.Schema;

namespace TerraformPluginDotnet;

public abstract class TerraformResource<TProviderState>
{
    public abstract string TypeName { get; }

    internal abstract ITerraformResource ToInternalResource();
}

public abstract class TerraformResource<TModel, TProviderState> : TerraformResource<TProviderState>
    where TModel : new()
{
    public TerraformComponentSchema Schema { get; } = TerraformDeclarativeSchema.For<TModel>();

    public virtual ValueTask<IReadOnlyList<TerraformDiagnostic>> ValidateConfigAsync(TModel config, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<TerraformDiagnostic>>([]);

    public abstract ValueTask<TerraformModelResult<TModel>> ReadAsync(
        TModel? currentState,
        TerraformResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    public abstract ValueTask<TerraformPlanResult<TModel>> PlanAsync(
        TModel? priorState,
        TModel? proposedState,
        TerraformResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    public abstract ValueTask<TerraformModelResult<TModel>> ApplyAsync(
        TModel? priorState,
        TModel? plannedState,
        TerraformResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    public virtual ValueTask<TerraformImportResult<TModel>> ImportAsync(
        string id,
        TerraformResourceContext<TProviderState> context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            new TerraformImportResult<TModel>(
                [],
                [
                    TerraformDiagnostic.Error(
                        "Import Not Supported",
                        "This resource does not implement import support."),
                ]));

    internal override ITerraformResource ToInternalResource() => new TypedResourceAdapter<TModel, TProviderState>(this);
}
