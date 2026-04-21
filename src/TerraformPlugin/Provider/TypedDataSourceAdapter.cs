using TerraformPlugin.Schema;
using TerraformPlugin.Types;

namespace TerraformPlugin.Provider;

internal sealed class TypedDataSourceAdapter<TModel, TProviderState>(DataSource<TModel, TProviderState> dataSource) : IDataSource
    where TModel : new()
{
    public ComponentSchema Schema => dataSource.Schema;

    public async ValueTask<ValidateResult> ValidateConfigAsync(DataSourceValidateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var config = ModelBinder.Bind<TModel>(request.Config);
            var diagnostics = await dataSource.ValidateConfigAsync(config, cancellationToken).ConfigureAwait(false);
            return new ValidateResult(diagnostics);
        }
        catch (Exception exception) when (!RuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new ValidateResult(
                RuntimeDiagnostics.FromException("Data source configuration validation failed", exception));
        }
    }

    public async ValueTask<ReadResult> ReadAsync(DataSourceReadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var config = ModelBinder.Bind<TModel>(request.Config);
            var result = await dataSource.ReadAsync(
                config,
                new DataSourceContext<TProviderState>(RequireProviderState(request.ProviderState)),
                cancellationToken).ConfigureAwait(false);

            return new ReadResult(
                result.Model is null
                    ? DynamicValue.Null(Schema.Block.ValueType())
                    : ModelBinder.Unbind(result.Model),
                Diagnostics: result.Diagnostics);
        }
        catch (Exception exception) when (!RuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new ReadResult(
                DynamicValue.Null(Schema.Block.ValueType()),
                Diagnostics: RuntimeDiagnostics.FromException("Data source read failed", exception));
        }
    }

    private static TProviderState RequireProviderState(object? providerState) =>
        providerState is TProviderState typed
            ? typed
            : throw new InvalidOperationException("Provider state was not available for the data source operation.");
}
