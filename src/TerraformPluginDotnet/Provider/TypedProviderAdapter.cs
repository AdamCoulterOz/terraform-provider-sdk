using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Provider;

internal sealed class TypedProviderAdapter<TConfig, TProviderState>(TerraformProvider<TConfig, TProviderState> provider) : ITerraformProvider
    where TConfig : new()
{
    private readonly IReadOnlyDictionary<string, ITerraformResource> _resources =
        provider.Resources.ToDictionary(
            static resource => resource.TypeName,
            static resource => resource.ToInternalResource(),
            StringComparer.Ordinal);

    private readonly IReadOnlyDictionary<string, ITerraformDataSource> _dataSources =
        provider.DataSources.ToDictionary(
            static dataSource => dataSource.TypeName,
            static dataSource => dataSource.ToInternalDataSource(),
            StringComparer.Ordinal);

    public TerraformComponentSchema ProviderSchema => provider.ProviderSchema;

    public TerraformComponentSchema? ProviderMetaSchema => provider.ProviderMetaSchema;

    public IReadOnlyDictionary<string, ITerraformResource> Resources => _resources;

    public IReadOnlyDictionary<string, ITerraformDataSource> DataSources => _dataSources;

    public async ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformProviderValidateRequest request, CancellationToken cancellationToken)
    {
        var config = TerraformModelBinder.Bind<TConfig>(request.Config);
        var diagnostics = await provider.ValidateConfigAsync(config, cancellationToken).ConfigureAwait(false);
        return new TerraformValidateResult(diagnostics);
    }

    public async ValueTask<TerraformConfigureResult> ConfigureAsync(TerraformProviderConfigureRequest request, CancellationToken cancellationToken)
    {
        var config = TerraformModelBinder.Bind<TConfig>(request.Config);
        var providerState = await provider.ConfigureAsync(
            config,
            new TerraformProviderContext(request.TerraformVersion, request.DeferralAllowed),
            cancellationToken).ConfigureAwait(false);

        return new TerraformConfigureResult(providerState, []);
    }
}
