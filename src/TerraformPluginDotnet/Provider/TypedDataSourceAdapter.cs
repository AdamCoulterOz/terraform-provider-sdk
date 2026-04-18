using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Provider;

internal sealed class TypedDataSourceAdapter<TModel, TProviderState>(TerraformDataSource<TModel, TProviderState> dataSource) : ITerraformDataSource
    where TModel : new()
{
    public TerraformComponentSchema Schema => dataSource.Schema;

    public async ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformDataSourceValidateRequest request, CancellationToken cancellationToken)
    {
        var config = TerraformModelBinder.Bind<TModel>(request.Config);
        var diagnostics = await dataSource.ValidateConfigAsync(config, cancellationToken).ConfigureAwait(false);
        return new TerraformValidateResult(diagnostics);
    }

    public async ValueTask<TerraformReadResult> ReadAsync(TerraformDataSourceReadRequest request, CancellationToken cancellationToken)
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

    private static TProviderState RequireProviderState(object? providerState) =>
        providerState is TProviderState typed
            ? typed
            : throw new InvalidOperationException("Provider state was not available for the data source operation.");
}
