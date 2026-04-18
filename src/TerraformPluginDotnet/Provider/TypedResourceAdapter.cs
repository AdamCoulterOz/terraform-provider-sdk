using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Provider;

internal sealed class TypedResourceAdapter<TModel, TProviderState>(TerraformResource<TModel, TProviderState> resource) : ITerraformResource
    where TModel : new()
{
    public TerraformComponentSchema Schema => resource.Schema;

    public async ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformResourceValidateRequest request, CancellationToken cancellationToken)
    {
        var config = TerraformModelBinder.Bind<TModel>(request.Config);
        var diagnostics = await resource.ValidateConfigAsync(config, cancellationToken).ConfigureAwait(false);
        return new TerraformValidateResult(diagnostics);
    }

    public async ValueTask<TerraformReadResult> ReadAsync(TerraformResourceReadRequest request, CancellationToken cancellationToken)
    {
        var currentState = request.CurrentState.IsNull
            ? default
            : TerraformModelBinder.Bind<TModel>(request.CurrentState);

        var result = await resource.ReadAsync(
            currentState,
            new TerraformResourceContext<TProviderState>(RequireProviderState(request.ProviderState)),
            cancellationToken).ConfigureAwait(false);

        return new TerraformReadResult(
            result.Model is null
                ? TerraformDynamicValue.Null(Schema.Block.ValueType())
                : TerraformModelBinder.Unbind(result.Model),
            Diagnostics: result.Diagnostics);
    }

    public async ValueTask<TerraformPlanResult> PlanAsync(TerraformResourcePlanRequest request, CancellationToken cancellationToken)
    {
        var priorState = request.PriorState.IsNull
            ? default
            : TerraformModelBinder.Bind<TModel>(request.PriorState);
        var proposedState = request.ProposedNewState.IsNull
            ? default
            : TerraformModelBinder.Bind<TModel>(request.ProposedNewState);

        var result = await resource.PlanAsync(
            priorState,
            proposedState,
            new TerraformResourceContext<TProviderState>(RequireProviderState(request.ProviderState)),
            cancellationToken).ConfigureAwait(false);

        return new TerraformPlanResult(
            result.PlannedState is null
                ? TerraformDynamicValue.Null(Schema.Block.ValueType())
                : TerraformModelBinder.Unbind(result.PlannedState),
            RequiresReplace: result.RequiresReplace,
            Diagnostics: result.Diagnostics);
    }

    public async ValueTask<TerraformApplyResult> ApplyAsync(TerraformResourceApplyRequest request, CancellationToken cancellationToken)
    {
        var priorState = request.PriorState.IsNull
            ? default
            : TerraformModelBinder.Bind<TModel>(request.PriorState);
        var plannedState = request.PlannedState.IsNull
            ? default
            : TerraformModelBinder.Bind<TModel>(request.PlannedState);

        var result = await resource.ApplyAsync(
            priorState,
            plannedState,
            new TerraformResourceContext<TProviderState>(RequireProviderState(request.ProviderState)),
            cancellationToken).ConfigureAwait(false);

        return new TerraformApplyResult(
            result.Model is null
                ? TerraformDynamicValue.Null(Schema.Block.ValueType())
                : TerraformModelBinder.Unbind(result.Model),
            Diagnostics: result.Diagnostics);
    }

    public async ValueTask<TerraformImportResult> ImportAsync(TerraformResourceImportRequest request, CancellationToken cancellationToken)
    {
        var result = await resource.ImportAsync(
            request.Id,
            new TerraformResourceContext<TProviderState>(RequireProviderState(request.ProviderState)),
            cancellationToken).ConfigureAwait(false);

        return new TerraformImportResult(
            result.Resources.Select(
                _ => new TerraformImportResource(
                    TerraformModelBinder.Unbind(_))).ToArray(),
            result.Diagnostics);
    }

    private static TProviderState RequireProviderState(object? providerState) =>
        providerState is TProviderState typed
            ? typed
            : throw new InvalidOperationException("Provider state was not available for the resource operation.");
}
