using TerraformPluginDotnet.Schema;

namespace TerraformPluginDotnet.Provider;

public interface ITerraformDataSource
{
    TerraformComponentSchema Schema { get; }

    ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformDataSourceValidateRequest request, CancellationToken cancellationToken);
    ValueTask<TerraformReadResult> ReadAsync(TerraformDataSourceReadRequest request, CancellationToken cancellationToken);
}

public abstract class TerraformDataSourceBase : ITerraformDataSource
{
    public abstract TerraformComponentSchema Schema { get; }

    public virtual ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformDataSourceValidateRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(TerraformValidateResult.Empty);

    public abstract ValueTask<TerraformReadResult> ReadAsync(TerraformDataSourceReadRequest request, CancellationToken cancellationToken);
}
