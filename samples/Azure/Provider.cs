using Azure.Storage.Blobs;
using TerraformPluginDotnet;
using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

namespace Azure;

internal sealed class Provider : TerraformProvider<ProviderConfigModel, ProviderState>
{
    public override string TypeName => "az";

    public override IEnumerable<TerraformResource<ProviderState>> Resources =>
        [new Storage.Blob.Blob()];

    public override IEnumerable<TerraformDataSource<ProviderState>> DataSources => [];

    public override ValueTask<IReadOnlyList<TerraformDiagnostic>> ValidateConfigAsync(ProviderConfigModel config, CancellationToken cancellationToken)
    {
        if (config.ConnectionString.IsUnknown || config.ConnectionString.IsNull)
            return ValueTask.FromResult<IReadOnlyList<TerraformDiagnostic>>([]);

        try
        {
            _ = new BlobServiceClient(config.ConnectionString.RequireValue());
            return ValueTask.FromResult<IReadOnlyList<TerraformDiagnostic>>([]);
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(
                (IReadOnlyList<TerraformDiagnostic>)
                [
                    TerraformDiagnostic.Error(
                        "Invalid Azure Storage connection string",
                        $"{exception.GetType().Name}: {exception.Message}",
                        TerraformAttributePath.Root("connection_string")),
                ]);
        }
    }

    public override ValueTask<ProviderState> ConfigureAsync(
        ProviderConfigModel config,
        TerraformProviderContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(ProviderState.Create(new BlobServiceClient(config.ConnectionString.RequireValue())));
}

internal sealed record ProviderState(BlobServiceClient ServiceClient, string AccountName)
{
    public static ProviderState Create(BlobServiceClient serviceClient) =>
        new(serviceClient, ResolveAccountName(serviceClient.Uri));

    private static string ResolveAccountName(Uri serviceUri)
    {
        var path = serviceUri.AbsolutePath.Trim('/');

        if (!string.IsNullOrEmpty(path))
        {
            var slash = path.IndexOf('/');
            return slash >= 0 ? path[..slash] : path;
        }

        var host = serviceUri.Host;
        var dot = host.IndexOf('.');

        if (dot > 0)
        {
            return host[..dot];
        }

        throw new InvalidOperationException($"Could not determine the Azure Storage account name from '{serviceUri}'.");
    }
}

internal sealed class ProviderConfigModel
{
    [TerraformAttribute(Description = "Azure Storage connection string.")]
    public TF<string> ConnectionString { get; init; }
}
