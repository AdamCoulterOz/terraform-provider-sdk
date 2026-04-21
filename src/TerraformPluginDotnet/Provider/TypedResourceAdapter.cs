using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Provider;

internal sealed class TypedResourceAdapter<TResource, TProviderState>(TerraformResource<TResource, TProviderState> resource) : ITerraformResource
    where TResource : TerraformResource<TResource, TProviderState>, new()
{
    public TerraformComponentSchema Schema => resource.Schema;

    public async ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformResourceValidateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var config = TerraformModelBinder.Bind<TResource>(request.Config);
            var diagnostics = await config.ValidateConfigAsync(cancellationToken).ConfigureAwait(false);
            return new TerraformValidateResult(diagnostics);
        }
        catch (Exception exception) when (!TerraformRuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new TerraformValidateResult(
                TerraformRuntimeDiagnostics.FromException("Resource configuration validation failed", exception));
        }
    }

    public async ValueTask<TerraformReadResult> ReadAsync(TerraformResourceReadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var currentState = request.CurrentState.IsNull
                ? default
                : TerraformModelBinder.Bind<TResource>(request.CurrentState);

            if (currentState is null)
            {
                return new TerraformReadResult(TerraformDynamicValue.Null(Schema.Block.ValueType()), PrivateState: request.PrivateState);
            }

            var result = await currentState.ReadAsync(
                new TerraformResourceContext<TProviderState>(
                    RequireProviderState(request.ProviderState),
                    PrivateState: request.PrivateState),
                cancellationToken).ConfigureAwait(false);

            return new TerraformReadResult(
                result.Model is null
                    ? TerraformDynamicValue.Null(Schema.Block.ValueType())
                    : TerraformModelBinder.Unbind(result.Model),
                PrivateState: result.PrivateState,
                Diagnostics: result.Diagnostics);
        }
        catch (Exception exception) when (!TerraformRuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new TerraformReadResult(
                TerraformDynamicValue.Null(Schema.Block.ValueType()),
                Diagnostics: TerraformRuntimeDiagnostics.FromException("Resource read failed", exception));
        }
    }

    public async ValueTask<TerraformPlanResult> PlanAsync(TerraformResourcePlanRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var priorState = request.PriorState.IsNull
                ? default
                : TerraformModelBinder.Bind<TResource>(request.PriorState);
            var proposedState = request.ProposedNewState.IsNull
                ? default
                : TerraformModelBinder.Bind<TResource>(request.ProposedNewState);

            var planTarget = proposedState ?? resource;

            var result = await planTarget.PlanAsync(
                priorState,
                new TerraformResourceContext<TProviderState>(
                    RequireProviderState(request.ProviderState),
                    PriorPrivateState: request.PriorPrivateState),
                cancellationToken).ConfigureAwait(false);

            return new TerraformPlanResult(
                result.PlannedState is null
                    ? TerraformDynamicValue.Null(Schema.Block.ValueType())
                    : TerraformModelBinder.Unbind(result.PlannedState),
                PlannedPrivateState: result.PlannedPrivateState,
                RequiresReplace: result.RequiresReplace,
                Diagnostics: result.Diagnostics);
        }
        catch (Exception exception) when (!TerraformRuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new TerraformPlanResult(
                TerraformDynamicValue.Null(Schema.Block.ValueType()),
                Diagnostics: TerraformRuntimeDiagnostics.FromException("Resource planning failed", exception));
        }
    }

    public async ValueTask<TerraformApplyResult> ApplyAsync(TerraformResourceApplyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var priorState = request.PriorState.IsNull
                ? default
                : TerraformModelBinder.Bind<TResource>(request.PriorState);
            var plannedState = request.PlannedState.IsNull
                ? default
                : TerraformModelBinder.Bind<TResource>(request.PlannedState);
            var context = new TerraformResourceContext<TProviderState>(
                RequireProviderState(request.ProviderState),
                PlannedPrivateState: request.PlannedPrivateState);

            var result = plannedState is null
                ? await resource.DeleteAsync(priorState, context, cancellationToken).ConfigureAwait(false)
                : await plannedState.ApplyAsync(priorState, context, cancellationToken).ConfigureAwait(false);

            return new TerraformApplyResult(
                result.Model is null
                    ? TerraformDynamicValue.Null(Schema.Block.ValueType())
                    : TerraformModelBinder.Unbind(result.Model),
                PrivateState: result.PrivateState,
                Diagnostics: result.Diagnostics);
        }
        catch (Exception exception) when (!TerraformRuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new TerraformApplyResult(
                TerraformDynamicValue.Null(Schema.Block.ValueType()),
                Diagnostics: TerraformRuntimeDiagnostics.FromException("Resource apply failed", exception));
        }
    }

    public async ValueTask<TerraformImportResult> ImportAsync(TerraformResourceImportRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await resource.ImportAsync(
                request.Id,
                new TerraformResourceContext<TProviderState>(RequireProviderState(request.ProviderState)),
                cancellationToken).ConfigureAwait(false);

            return new TerraformImportResult(
                result.Resources.Select(static resourceModel => new TerraformImportResource(TerraformModelBinder.Unbind(resourceModel))).ToArray(),
                result.Diagnostics);
        }
        catch (Exception exception) when (!TerraformRuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new TerraformImportResult(
                [],
                TerraformRuntimeDiagnostics.FromException("Resource import failed", exception));
        }
    }

    private static TProviderState RequireProviderState(object? providerState) =>
        providerState is TProviderState typed
            ? typed
            : throw new InvalidOperationException("Provider state was not available for the resource operation.");
}
