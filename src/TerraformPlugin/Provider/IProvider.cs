using TerraformPlugin.Schema;

namespace TerraformPlugin.Provider;

internal interface IProvider
{
    ComponentSchema ProviderSchema { get; }
    ComponentSchema? ProviderMetaSchema { get; }
    IReadOnlyDictionary<string, IResource> Resources { get; }
    IReadOnlyDictionary<string, IDataSource> DataSources { get; }
    IReadOnlyDictionary<string, IListResource> ListResources { get; }

    ValueTask<ValidateResult> ValidateConfigAsync(ProviderValidateRequest request, CancellationToken cancellationToken);
    ValueTask<ConfigureResult> ConfigureAsync(ProviderConfigureRequest request, CancellationToken cancellationToken);
}

internal abstract class ProviderBase : IProvider
{
    public abstract ComponentSchema ProviderSchema { get; }
    public virtual ComponentSchema? ProviderMetaSchema => null;
    public abstract IReadOnlyDictionary<string, IResource> Resources { get; }
    public abstract IReadOnlyDictionary<string, IDataSource> DataSources { get; }
    public abstract IReadOnlyDictionary<string, IListResource> ListResources { get; }

    public virtual ValueTask<ValidateResult> ValidateConfigAsync(ProviderValidateRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ValidateResult.Empty);

    public abstract ValueTask<ConfigureResult> ConfigureAsync(ProviderConfigureRequest request, CancellationToken cancellationToken);
}
