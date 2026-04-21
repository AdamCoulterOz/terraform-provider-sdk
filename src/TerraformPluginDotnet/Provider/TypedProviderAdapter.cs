using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Provider;

internal sealed class TypedProviderAdapter<TConfig, TProviderState>(TerraformProvider<TConfig, TProviderState> provider) : ITerraformProvider
    where TConfig : new()
{
    private readonly string _providerTypeName = provider.TypeName;
    private readonly string _componentTypeNamePrefix = provider.ComponentTypeNamePrefix;

    private readonly IReadOnlyDictionary<string, ITerraformResource> _resources =
        provider.Resources.ToDictionary(
            resource => TerraformTypeNames.Compose(provider.ComponentTypeNamePrefix, resource.Name),
            static resource => resource.ToInternalResource(),
            StringComparer.Ordinal);

    private readonly IReadOnlyDictionary<string, ITerraformDataSource> _dataSources =
        BuildDataSources(provider);

    public TerraformComponentSchema ProviderSchema => provider.ProviderSchema;

    public TerraformComponentSchema? ProviderMetaSchema => provider.ProviderMetaSchema;

    public IReadOnlyDictionary<string, ITerraformResource> Resources => _resources;

    public IReadOnlyDictionary<string, ITerraformDataSource> DataSources => _dataSources;

    public string ProviderTypeName => _providerTypeName;

    private static IReadOnlyDictionary<string, ITerraformDataSource> BuildDataSources(TerraformProvider<TConfig, TProviderState> provider)
    {
        var dataSources = new Dictionary<string, ITerraformDataSource>(StringComparer.Ordinal);

        foreach (var dataSource in provider.DataSources)
        {
            dataSources.Add(
                TerraformTypeNames.Compose(provider.ComponentTypeNamePrefix, dataSource.Name),
                dataSource.ToInternalDataSource());
        }

        foreach (var resource in provider.Resources)
        {
            foreach (var generated in resource.ToGeneratedDataSources())
            {
                dataSources.Add(
                    TerraformTypeNames.Compose(provider.ComponentTypeNamePrefix, generated.Name),
                    generated.DataSource);
            }
        }

        return dataSources;
    }

    public async ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformProviderValidateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var config = TerraformModelBinder.Bind<TConfig>(request.Config);
            var diagnostics = await provider.ValidateConfigAsync(config, cancellationToken).ConfigureAwait(false);
            return new TerraformValidateResult(diagnostics);
        }
        catch (Exception exception) when (!TerraformRuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new TerraformValidateResult(
                TerraformRuntimeDiagnostics.FromException("Provider configuration validation failed", exception));
        }
    }

    public async ValueTask<TerraformConfigureResult> ConfigureAsync(TerraformProviderConfigureRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var config = TerraformModelBinder.Bind<TConfig>(request.Config);
            var providerState = await provider.ConfigureAsync(
                config,
                new TerraformProviderContext(request.TerraformVersion, request.DeferralAllowed),
                cancellationToken).ConfigureAwait(false);

            return new TerraformConfigureResult(providerState, []);
        }
        catch (Exception exception) when (!TerraformRuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new TerraformConfigureResult(
                null,
                TerraformRuntimeDiagnostics.FromException("Provider configuration failed", exception));
        }
    }
}
