using TerraformPlugin.Schema;

namespace TerraformPlugin.Provider;

internal interface IDataSource
{
    ComponentSchema Schema { get; }

    ValueTask<ValidateResult> ValidateConfigAsync(DataSourceValidateRequest request, CancellationToken cancellationToken);
    ValueTask<ReadResult> ReadAsync(DataSourceReadRequest request, CancellationToken cancellationToken);
}

internal abstract class TerraformDataSourceBase : IDataSource
{
    public abstract ComponentSchema Schema { get; }

    public virtual ValueTask<ValidateResult> ValidateConfigAsync(DataSourceValidateRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ValidateResult.Empty);

    public abstract ValueTask<ReadResult> ReadAsync(DataSourceReadRequest request, CancellationToken cancellationToken);
}
