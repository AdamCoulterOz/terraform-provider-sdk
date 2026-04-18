using TerraformPluginDotnet.Schema;

namespace TerraformPluginDotnet.Provider;

public interface ITerraformProvider
{
    TerraformComponentSchema ProviderSchema { get; }
    TerraformComponentSchema? ProviderMetaSchema { get; }
    IReadOnlyDictionary<string, ITerraformResource> Resources { get; }
    IReadOnlyDictionary<string, ITerraformDataSource> DataSources { get; }

    ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformProviderValidateRequest request, CancellationToken cancellationToken);
    ValueTask<TerraformConfigureResult> ConfigureAsync(TerraformProviderConfigureRequest request, CancellationToken cancellationToken);
}

public abstract class TerraformProviderBase : ITerraformProvider
{
    public abstract TerraformComponentSchema ProviderSchema { get; }
    public virtual TerraformComponentSchema? ProviderMetaSchema => null;
    public abstract IReadOnlyDictionary<string, ITerraformResource> Resources { get; }
    public abstract IReadOnlyDictionary<string, ITerraformDataSource> DataSources { get; }

    public virtual ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformProviderValidateRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(TerraformValidateResult.Empty);

    public abstract ValueTask<TerraformConfigureResult> ConfigureAsync(TerraformProviderConfigureRequest request, CancellationToken cancellationToken);
}
