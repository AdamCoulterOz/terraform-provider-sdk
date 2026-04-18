using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Provider;
using TerraformPluginDotnet.Schema;

namespace TerraformPluginDotnet;

public abstract class TerraformDataSource<TProviderState>
{
    public abstract string TypeName { get; }

    internal abstract ITerraformDataSource ToInternalDataSource();
}

public abstract class TerraformDataSource<TModel, TProviderState> : TerraformDataSource<TProviderState>
    where TModel : new()
{
    public TerraformComponentSchema Schema { get; } = TerraformDeclarativeSchema.For<TModel>();

    public virtual ValueTask<IReadOnlyList<TerraformDiagnostic>> ValidateConfigAsync(TModel config, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<TerraformDiagnostic>>([]);

    public abstract ValueTask<TerraformModelResult<TModel>> ReadAsync(
        TModel config,
        TerraformDataSourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    internal override ITerraformDataSource ToInternalDataSource() => new TypedDataSourceAdapter<TModel, TProviderState>(this);
}
