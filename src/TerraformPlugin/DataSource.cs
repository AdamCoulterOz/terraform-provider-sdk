using TerraformPlugin.Diagnostics;
using TerraformPlugin.Provider;
using TerraformPlugin.Schema;
using TerraformPlugin.Validation;

namespace TerraformPlugin;

public abstract class DataSource<TProviderState>
{
    public abstract string Name { get; }
    internal abstract IDataSource ToInternalDataSource();
}

internal sealed class RegisteredTerraformDataSource<TDataSource, TProviderState> : DataSource<TProviderState>
    where TDataSource : DataSource<TProviderState>
{
    private readonly Lazy<TDataSource> _definition = new(CreateDefinition, LazyThreadSafetyMode.ExecutionAndPublication);

    public override string Name => Definition.Name;

    internal override IDataSource ToInternalDataSource() => Definition.ToInternalDataSource();

    private TDataSource Definition => _definition.Value;

    private static TDataSource CreateDefinition() =>
        (TDataSource)(Activator.CreateInstance(typeof(TDataSource), nonPublic: true)
            ?? throw new InvalidOperationException($"Could not create data source definition '{typeof(TDataSource).FullName}'."));
}

public abstract class DataSource<TModel, TProviderState> : DataSource<TProviderState>
    where TModel : new()
{
    public ComponentSchema Schema { get; } = DeclarativeSchema.For<TModel>();

    public virtual ValueTask<IReadOnlyList<Diagnostic>> ValidateConfigAsync(TModel config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        return ValueTask.FromResult(Validator.Validate(config));
    }

    public abstract ValueTask<ModelResult<TModel>> ReadAsync(
        TModel config,
        DataSourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    internal override IDataSource ToInternalDataSource() => new TypedDataSourceAdapter<TModel, TProviderState>(this);
}
