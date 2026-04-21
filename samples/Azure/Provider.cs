using Azure.Storage.Blobs;
using TerraformPlugin;
using TerraformPlugin.Diagnostics;
using TerraformPlugin.Schema;
using TerraformPlugin.Types;
using TerraformPlugin.Validation;

namespace Azure;

internal sealed class Provider : Provider<ProviderConfigModel, ProviderState>
{
    public override string TypeName => "az";

    public override IEnumerable<Resource<ProviderState>> Resources =>
        [
            Resource<Storage.Account>(),
            Resource<Storage.Blob.Blob>(),
        ];

    public override IEnumerable<DataSource<ProviderState>> DataSources => [];

    public override ValueTask<IReadOnlyList<Diagnostic>> ValidateConfigAsync(ProviderConfigModel config, CancellationToken cancellationToken)
    {
        var diagnostics = Validator.Validate(config);

        if (diagnostics.Count > 0 || config.ConnectionString.IsUnknown || config.ConnectionString.IsNull)
            return ValueTask.FromResult(diagnostics);

        try
        {
            _ = new BlobServiceClient(config.ConnectionString.RequireValue());
            return ValueTask.FromResult(diagnostics);
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult<IReadOnlyList<Diagnostic>>(
            [
                .. diagnostics,
                Diagnostic.Error(
                    "Invalid Azure Storage connection string",
                    $"{exception.GetType().Name}: {exception.Message}",
                    AttributePath.Root("connection_string")),
            ]);
        }
    }

    public override ValueTask<ProviderState> ConfigureAsync(
        ProviderConfigModel config,
        ProviderContext context,
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
            return host[..dot];

        throw new InvalidOperationException($"Could not determine the Azure Storage account name from '{serviceUri}'.");
    }
}

internal sealed class ProviderConfigModel
{
    [TFAttribute(Description = "Azure Storage connection string.")]
    [NotEmpty]
    public TF<string> ConnectionString { get; init; }
}
