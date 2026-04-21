using TerraformPlugin.Schema;
using TerraformPlugin.Types;

namespace TerraformPlugin.Provider;

internal sealed class TypedResourceAdapter<TResource, TProviderState>(Resource<TResource, TProviderState> resource) : IResource
    where TResource : Resource<TResource, TProviderState>
{
    public ComponentSchema Schema => resource.Schema;

    public IdentitySchema? IdentitySchema => resource.IdentitySchema;

    public async ValueTask<ValidateResult> ValidateConfigAsync(ValidateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var config = ModelBinder.Bind<TResource>(request.Config);
            var diagnostics = await config.ValidateConfigAsync(cancellationToken).ConfigureAwait(false);
            return new ValidateResult(diagnostics);
        }
        catch (Exception exception) when (!RuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new ValidateResult(
                RuntimeDiagnostics.FromException("Resource configuration validation failed", exception));
        }
    }

    public async ValueTask<ReadResult> ReadAsync(ResourceReadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var currentState = request.CurrentState.IsNull
                ? default
                : ModelBinder.Bind<TResource>(request.CurrentState);

            if (currentState is null)
            {
                return new ReadResult(DynamicValue.Null(Schema.Block.ValueType()), PrivateState: request.PrivateState);
            }

            var result = await currentState.ReadAsync(
                new ResourceContext<TProviderState>(
                    RequireProviderState(request.ProviderState),
                    PrivateState: request.PrivateState),
                cancellationToken).ConfigureAwait(false);

            return new ReadResult(
                result.Model is null
                    ? DynamicValue.Null(Schema.Block.ValueType())
                    : ModelBinder.Unbind(result.Model),
                PrivateState: result.PrivateState,
                Diagnostics: result.Diagnostics);
        }
        catch (Exception exception) when (!RuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new ReadResult(
                DynamicValue.Null(Schema.Block.ValueType()),
                Diagnostics: RuntimeDiagnostics.FromException("Resource read failed", exception));
        }
    }

    public async ValueTask<PlanResult> PlanAsync(ResourcePlanRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var priorState = request.PriorState.IsNull
                ? default
                : ModelBinder.Bind<TResource>(request.PriorState);
            var proposedState = request.ProposedNewState.IsNull
                ? default
                : ModelBinder.Bind<TResource>(request.ProposedNewState);

            var planTarget = proposedState ?? resource;

            var result = await planTarget.PlanAsync(
                priorState,
                new ResourceContext<TProviderState>(
                    RequireProviderState(request.ProviderState),
                    PriorPrivateState: request.PriorPrivateState),
                cancellationToken).ConfigureAwait(false);

            return new PlanResult(
                result.PlannedState is null
                    ? DynamicValue.Null(Schema.Block.ValueType())
                    : ModelBinder.Unbind(result.PlannedState),
                PlannedPrivateState: result.PlannedPrivateState,
                RequiresReplace: result.RequiresReplace,
                Diagnostics: result.Diagnostics);
        }
        catch (Exception exception) when (!RuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new PlanResult(
                DynamicValue.Null(Schema.Block.ValueType()),
                Diagnostics: RuntimeDiagnostics.FromException("Resource planning failed", exception));
        }
    }

    public async ValueTask<ApplyResult> ApplyAsync(ResourceApplyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var priorState = request.PriorState.IsNull
                ? default
                : ModelBinder.Bind<TResource>(request.PriorState);
            var plannedState = request.PlannedState.IsNull
                ? default
                : ModelBinder.Bind<TResource>(request.PlannedState);
            var context = new ResourceContext<TProviderState>(
                RequireProviderState(request.ProviderState),
                PlannedPrivateState: request.PlannedPrivateState);

            var result = plannedState is null
                ? await resource.DeleteAsync(priorState, context, cancellationToken).ConfigureAwait(false)
                : await plannedState.ApplyAsync(priorState, context, cancellationToken).ConfigureAwait(false);

            return new ApplyResult(
                result.Model is null
                    ? DynamicValue.Null(Schema.Block.ValueType())
                    : ModelBinder.Unbind(result.Model),
                PrivateState: result.PrivateState,
                Diagnostics: result.Diagnostics);
        }
        catch (Exception exception) when (!RuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new ApplyResult(
                DynamicValue.Null(Schema.Block.ValueType()),
                Diagnostics: RuntimeDiagnostics.FromException("Resource apply failed", exception));
        }
    }

    public async ValueTask<ImportResult> ImportAsync(ResourceImportRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await resource.ImportAsync(
                request.Id,
                new ResourceContext<TProviderState>(RequireProviderState(request.ProviderState)),
                cancellationToken).ConfigureAwait(false);

            return new ImportResult(
                result.Resources.Select(static resourceModel => new ImportResource(ModelBinder.Unbind(resourceModel))).ToArray(),
                result.Diagnostics);
        }
        catch (Exception exception) when (!RuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new ImportResult(
                [],
                RuntimeDiagnostics.FromException("Resource import failed", exception));
        }
    }

    private static TProviderState RequireProviderState(object? providerState) =>
        providerState is TProviderState typed
            ? typed
            : throw new InvalidOperationException("Provider state was not available for the resource operation.");
}
