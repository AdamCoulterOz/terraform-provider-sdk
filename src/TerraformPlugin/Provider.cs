using TerraformPlugin.Diagnostics;
using TerraformPlugin.Provider;
using TerraformPlugin.Schema;
using TerraformPlugin.Validation;

namespace TerraformPlugin;

public abstract class Provider<TConfig, TProviderState>
    where TConfig : new()
{
    public abstract string TypeName { get; }

    public virtual string ComponentTypeNamePrefix => TypeName;

    public ComponentSchema ProviderSchema { get; } = DeclarativeSchema.For<TConfig>();

    public virtual ComponentSchema? ProviderMetaSchema => null;

    public abstract IEnumerable<Resource<TProviderState>> Resources { get; }

    public abstract IEnumerable<DataSource<TProviderState>> DataSources { get; }

    public virtual ValueTask<IReadOnlyList<Diagnostic>> ValidateConfigAsync(TConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        return ValueTask.FromResult(Validator.Validate(config));
    }

    public abstract ValueTask<TProviderState> ConfigureAsync(
        TConfig config,
        ProviderContext context,
        CancellationToken cancellationToken);

    protected static Resource<TProviderState> Resource<TResource>()
        where TResource : Resource<TResource, TProviderState> =>
        new RegisteredTerraformResource<TResource, TProviderState>();

    protected static DataSource<TProviderState> DataSource<TDataSource>()
        where TDataSource : DataSource<TProviderState> =>
        new RegisteredTerraformDataSource<TDataSource, TProviderState>();

    internal IProvider ToInternalProvider() => new TypedProviderAdapter<TConfig, TProviderState>(this);
}
