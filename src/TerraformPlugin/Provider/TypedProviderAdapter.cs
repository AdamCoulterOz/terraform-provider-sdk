using TerraformPlugin.Schema;

namespace TerraformPlugin.Provider;

internal sealed class TypedProviderAdapter<TConfig, TProviderState> : IProvider
    where TConfig : new()
{
    private readonly Provider<TConfig, TProviderState> _provider;
    private readonly string _providerTypeName;
    private readonly IReadOnlyDictionary<string, IResource> _resources;
    private readonly IReadOnlyDictionary<string, IDataSource> _dataSources;
    private readonly IReadOnlyDictionary<string, IListResource> _listResources;

    public TypedProviderAdapter(Provider<TConfig, TProviderState> provider)
    {
        _provider = provider;
        _providerTypeName = provider.TypeName;

        var resources = provider.Resources.ToArray();
        var dataSources = provider.DataSources.ToArray();

        _resources = resources.ToDictionary(
            resource => TFTypeNames.Compose(provider.ComponentTypeNamePrefix, resource.Name),
            static resource => resource.ToInternalResource(),
            StringComparer.Ordinal);

        _dataSources = BuildDataSources(provider.ComponentTypeNamePrefix, resources, dataSources);
        _listResources = BuildListResources(provider.ComponentTypeNamePrefix, resources);
    }

    public ComponentSchema ProviderSchema => _provider.ProviderSchema;

    public ComponentSchema? ProviderMetaSchema => _provider.ProviderMetaSchema;

    public IReadOnlyDictionary<string, IResource> Resources => _resources;

    public IReadOnlyDictionary<string, IDataSource> DataSources => _dataSources;

    public IReadOnlyDictionary<string, IListResource> ListResources => _listResources;

    public string ProviderTypeName => _providerTypeName;

    private static IReadOnlyDictionary<string, IDataSource> BuildDataSources(
        string componentTypeNamePrefix,
        IEnumerable<Resource<TProviderState>> resources,
        IEnumerable<DataSource<TProviderState>> dataSources)
    {
        var resolved = new Dictionary<string, IDataSource>(StringComparer.Ordinal);

        foreach (var dataSource in dataSources)
        {
            resolved.Add(
                TFTypeNames.Compose(componentTypeNamePrefix, dataSource.Name),
                dataSource.ToInternalDataSource());
        }

        foreach (var resource in resources)
        {
            foreach (var generated in resource.ToGeneratedDataSources())
            {
                resolved.Add(
                    TFTypeNames.Compose(componentTypeNamePrefix, generated.Name),
                    generated.DataSource);
            }
        }

        return resolved;
    }

    private static IReadOnlyDictionary<string, IListResource> BuildListResources(
        string componentTypeNamePrefix,
        IEnumerable<Resource<TProviderState>> resources)
    {
        var resolved = new Dictionary<string, IListResource>(StringComparer.Ordinal);

        foreach (var resource in resources)
        {
            foreach (var generated in resource.ToGeneratedListResources())
            {
                resolved.Add(
                    TFTypeNames.Compose(componentTypeNamePrefix, generated.Name),
                    generated.ListResource);
            }
        }

        return resolved;
    }

    public async ValueTask<ValidateResult> ValidateConfigAsync(ProviderValidateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var config = ModelBinder.Bind<TConfig>(request.Config);
            var diagnostics = await _provider.ValidateConfigAsync(config, cancellationToken).ConfigureAwait(false);
            return new ValidateResult(diagnostics);
        }
        catch (Exception exception) when (!RuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new ValidateResult(
                RuntimeDiagnostics.FromException("Provider configuration validation failed", exception));
        }
    }

    public async ValueTask<ConfigureResult> ConfigureAsync(ProviderConfigureRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var config = ModelBinder.Bind<TConfig>(request.Config);
            var providerState = await _provider.ConfigureAsync(
                config,
                new ProviderContext(request.TerraformVersion, request.DeferralAllowed),
                cancellationToken).ConfigureAwait(false);

            return new ConfigureResult(providerState, []);
        }
        catch (Exception exception) when (!RuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new ConfigureResult(
                null,
                RuntimeDiagnostics.FromException("Provider configuration failed", exception));
        }
    }
}
