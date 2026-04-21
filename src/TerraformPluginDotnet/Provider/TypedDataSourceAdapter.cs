using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Provider;

internal sealed class TypedDataSourceAdapter<TModel, TProviderState>(TerraformDataSource<TModel, TProviderState> dataSource) : ITerraformDataSource
    where TModel : new()
{
    public TerraformComponentSchema Schema => dataSource.Schema;

    public async ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformDataSourceValidateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var config = TerraformModelBinder.Bind<TModel>(request.Config);
            var diagnostics = await dataSource.ValidateConfigAsync(config, cancellationToken).ConfigureAwait(false);
            return new TerraformValidateResult(diagnostics);
        }
        catch (Exception exception) when (!TerraformRuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new TerraformValidateResult(
                TerraformRuntimeDiagnostics.FromException("Data source configuration validation failed", exception));
        }
    }

    public async ValueTask<TerraformReadResult> ReadAsync(TerraformDataSourceReadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var config = TerraformModelBinder.Bind<TModel>(request.Config);
            var result = await dataSource.ReadAsync(
                config,
                new TerraformDataSourceContext<TProviderState>(RequireProviderState(request.ProviderState)),
                cancellationToken).ConfigureAwait(false);

            return new TerraformReadResult(
                result.Model is null
                    ? TerraformDynamicValue.Null(Schema.Block.ValueType())
                    : TerraformModelBinder.Unbind(result.Model),
                Diagnostics: result.Diagnostics);
        }
        catch (Exception exception) when (!TerraformRuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new TerraformReadResult(
                TerraformDynamicValue.Null(Schema.Block.ValueType()),
                Diagnostics: TerraformRuntimeDiagnostics.FromException("Data source read failed", exception));
        }
    }

    private static TProviderState RequireProviderState(object? providerState) =>
        providerState is TProviderState typed
            ? typed
            : throw new InvalidOperationException("Provider state was not available for the data source operation.");
}
