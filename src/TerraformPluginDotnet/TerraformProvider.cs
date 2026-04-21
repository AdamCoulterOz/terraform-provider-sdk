using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Provider;
using TerraformPluginDotnet.Schema;

namespace TerraformPluginDotnet;

public abstract class TerraformProvider<TConfig, TProviderState>
    where TConfig : new()
{
    public abstract string TypeName { get; }

    public virtual string ComponentTypeNamePrefix => TypeName;

    public TerraformComponentSchema ProviderSchema { get; } = TerraformDeclarativeSchema.For<TConfig>();

    public virtual TerraformComponentSchema? ProviderMetaSchema => null;

    public abstract IEnumerable<TerraformResource<TProviderState>> Resources { get; }

    public abstract IEnumerable<TerraformDataSource<TProviderState>> DataSources { get; }

    public virtual ValueTask<IReadOnlyList<TerraformDiagnostic>> ValidateConfigAsync(TConfig config, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<TerraformDiagnostic>>([]);

    public abstract ValueTask<TProviderState> ConfigureAsync(
        TConfig config,
        TerraformProviderContext context,
        CancellationToken cancellationToken);

    internal ITerraformProvider ToInternalProvider() => new TypedProviderAdapter<TConfig, TProviderState>(this);
}
