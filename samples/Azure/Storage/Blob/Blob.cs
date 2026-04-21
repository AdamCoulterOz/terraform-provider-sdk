using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.ResourceIds;
using TerraformPlugin;
using TerraformPlugin.Diagnostics;
using TerraformPlugin.Schema;
using TerraformPlugin.Types;
using TerraformPlugin.Validation;
using StorageProvider = Azure.Storage.Storage;

namespace Azure.Storage.Blob;

internal sealed class Blob : AzureResource<Blob, StorageProvider>
    , IDataSource<Blob, Blob.Get>
{
    [NotEmpty]
    public required TF<string> ContainerName { get; init; }

    [NotEmpty]
    public required TF<string> BlobName { get; init; }

    public required TF<string> Content { get; init; }

    [TFAttribute(Computed = true, Description = "Azure resource identifier.")]
    public TF<string> Id { get; init; }

    [TFAttribute(Computed = true, Description = "Blob entity tag.")]
    public TF<string> ETag { get; init; }

    public override async ValueTask<ModelResult<Blob>> ReadAsync(
        ResourceContext<ProviderState> context,
        CancellationToken cancellationToken)
    {
        if (ContainerName.IsNull || ContainerName.IsUnknown || BlobName.IsNull || BlobName.IsUnknown)
            return new ModelResult<Blob>(null, PrivateState: context.PrivateState);

        var state = await ReadExistingAsync(
            context.ProviderState,
            ContainerName.RequireValue(),
            BlobName.RequireValue(),
            cancellationToken).ConfigureAwait(false);

        return new ModelResult<Blob>(
            state is null ? null : ToResource(state),
            PrivateState: context.PrivateState);
    }

    public override ValueTask<PlanResult<Blob>> PlanAsync(
        Blob? priorState,
        ResourceContext<ProviderState> context,
        CancellationToken cancellationToken)
    {
        if (ContainerName.IsUnknown || BlobName.IsUnknown || Content.IsUnknown)
            return ValueTask.FromResult(
                new PlanResult<Blob>(
                    UnknownPlannedState(context.ProviderState, ContainerName, BlobName, Content),
                    PlannedPrivateState: context.PriorPrivateState));

        var requiresReplace = GetReplacePaths(
            priorState,
            ContainerName.RequireValue(),
            BlobName.RequireValue());
        var etag = GetPlannedETag(priorState, this);

        return ValueTask.FromResult(
            new PlanResult<Blob>(
                new Blob
                {
                    ContainerName = ContainerName,
                    BlobName = BlobName,
                    Content = Content,
                    Id = TF<string>.Known(CreateBlobId(
                        context.ProviderState,
                        ContainerName.RequireValue(),
                        BlobName.RequireValue())),
                    ETag = etag,
                },
                PlannedPrivateState: context.PriorPrivateState,
                RequiresReplace: requiresReplace));
    }

    public override async ValueTask<ModelResult<Blob>> ApplyAsync(
        Blob? priorState,
        ResourceContext<ProviderState> context,
        CancellationToken cancellationToken)
    {
        var containerName = ContainerName.RequireValue();
        var blobName = BlobName.RequireValue();
        var content = Content.RequireValue();
        var containerClient = context.ProviderState.ServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await blobClient.UploadAsync(BinaryData.FromString(content), overwrite: true, cancellationToken).ConfigureAwait(false);

        var state = await ReadExistingAsync(context.ProviderState, containerName, blobName, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Azure Storage blob was not readable after upload.");

        return new ModelResult<Blob>(
            ToResource(state),
            PrivateState: context.PlannedPrivateState);
    }

    public override async ValueTask<ModelResult<Blob>> DeleteAsync(
        Blob? priorState,
        ResourceContext<ProviderState> context,
        CancellationToken cancellationToken)
    {
        if (priorState is not null &&
            priorState.ContainerName.IsKnown &&
            priorState.BlobName.IsKnown)
        {
            var existingBlobClient = GetBlobClient(
                context.ProviderState,
                priorState.ContainerName.RequireValue(),
                priorState.BlobName.RequireValue());

            await existingBlobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return new ModelResult<Blob>(null, PrivateState: context.PlannedPrivateState);
    }

    public override async ValueTask<ImportResult<Blob>> ImportAsync(
        string id,
        ResourceContext<ProviderState> context,
        CancellationToken cancellationToken)
    {
        if (!TryParseBlobId(id, out var accountName, out var containerName, out var blobName))
            return new ImportResult<Blob>(
                [],
                [
                    Diagnostic.Error(
                        "Invalid import id",
                        "Expected either an Azure-style resource id or the shorthand '<container>/<blob>'."),
                ]);

        if (!string.IsNullOrEmpty(accountName) &&
            !string.Equals(accountName, context.ProviderState.AccountName, StringComparison.Ordinal))
            return new ImportResult<Blob>(
                [],
                [
                    Diagnostic.Error(
                        "Import target account mismatch",
                        $"The import id references storage account '{accountName}', but the configured provider is connected to '{context.ProviderState.AccountName}'."),
                ]);

        var state = await ReadExistingAsync(context.ProviderState, containerName, blobName, cancellationToken).ConfigureAwait(false);

        if (state is null)
            return new ImportResult<Blob>(
                [],
                [
                    Diagnostic.Error(
                        "Import target not found",
                        $"No blob exists at '{id}'."),
                ]);

        return new ImportResult<Blob>([ToResource(state)]);
    }

    public static async ValueTask<Blob?> GetAsync(
        Get parameters,
        DataSourceContext context,
        CancellationToken cancellationToken)
    {
        var providerState = context.RequireProviderState<ProviderState>();
        var state = await ReadExistingAsync(
            providerState,
            parameters.ContainerName.RequireValue(),
            parameters.BlobName.RequireValue(),
            cancellationToken).ConfigureAwait(false);

        return state is null ? null : ToResource(state);
    }

    internal readonly record struct Get(
        [property: NotEmpty] TF<string> ContainerName,
        [property: NotEmpty] TF<string> BlobName);

    private static string CreateBlobId(ProviderState providerState, string containerName, string blobName) =>
        StorageProvider.Instance.Blob.FormatResourceId(
            providerState.AccountName,
            "default",
            containerName,
            blobName);

    private static bool TryParseBlobId(string id, out string accountName, out string containerName, out string blobName)
    {
        if (StorageProvider.Instance.Blob.TryParseResourceId(id, out var names))
        {
            accountName = names[0];
            containerName = names[2];
            blobName = names[3];
            return true;
        }

        var separator = id.IndexOf('/', StringComparison.Ordinal);

        if (separator <= 0 || separator == id.Length - 1)
        {
            accountName = string.Empty;
            containerName = string.Empty;
            blobName = string.Empty;
            return false;
        }

        accountName = string.Empty;
        containerName = id[..separator];
        blobName = id[(separator + 1)..];
        return true;
    }

    private static BlobClient GetBlobClient(ProviderState providerState, string containerName, string blobName) =>
        providerState.ServiceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobName);

    private static async Task<BlobSnapshot?> ReadExistingAsync(
        ProviderState providerState,
        string containerName,
        string blobName,
        CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(providerState, containerName, blobName);

        if (!await blobClient.ExistsAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var download = await blobClient.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return new BlobSnapshot(
            containerName,
            blobName,
            download.Value.Content.ToString(),
            CreateBlobId(providerState, containerName, blobName),
            properties.Value.ETag.ToString());
    }

    private static Blob ToResource(BlobSnapshot snapshot) =>
        new()
        {
            ContainerName = TF<string>.Known(snapshot.ContainerName),
            BlobName = TF<string>.Known(snapshot.BlobName),
            Content = TF<string>.Known(snapshot.Content),
            Id = TF<string>.Known(snapshot.Id),
            ETag = TF<string>.Known(snapshot.ETag),
        };

    private static Blob UnknownPlannedState(ProviderState providerState, TF<string> containerName, TF<string> blobName, TF<string> content) =>
        new()
        {
            ContainerName = containerName,
            BlobName = blobName,
            Content = content,
            Id = containerName.IsKnown && blobName.IsKnown
                ? TF<string>.Known(CreateBlobId(providerState, containerName.RequireValue(), blobName.RequireValue()))
                : TF<string>.Unknown(),
            ETag = TF<string>.Unknown(),
        };

    private static IReadOnlyList<AttributePath>? GetReplacePaths(Blob? priorState, string nextContainerName, string nextBlobName)
    {
        if (priorState is null ||
            priorState.ContainerName.IsUnknown ||
            priorState.BlobName.IsUnknown ||
            priorState.ContainerName.IsNull ||
            priorState.BlobName.IsNull)
            return null;

        var requiresReplace = new List<AttributePath>();

        if (!string.Equals(priorState.ContainerName.RequireValue(), nextContainerName, StringComparison.Ordinal))
            requiresReplace.Add(AttributePath.Root("container_name"));

        if (!string.Equals(priorState.BlobName.RequireValue(), nextBlobName, StringComparison.Ordinal))
            requiresReplace.Add(AttributePath.Root("blob_name"));

        return requiresReplace.Count == 0 ? null : requiresReplace;
    }

    private static TF<string> GetPlannedETag(Blob? priorState, Blob proposedState)
    {
        if (priorState is null ||
            !priorState.ETag.IsKnown ||
            !priorState.ContainerName.IsKnown ||
            !priorState.BlobName.IsKnown ||
            !priorState.Content.IsKnown ||
            proposedState.ContainerName.IsUnknown ||
            proposedState.BlobName.IsUnknown ||
            proposedState.Content.IsUnknown ||
            proposedState.ContainerName.IsNull ||
            proposedState.BlobName.IsNull ||
            proposedState.Content.IsNull)
            return TF<string>.Unknown();

        var containerMatches = string.Equals(
            priorState.ContainerName.RequireValue(),
            proposedState.ContainerName.RequireValue(),
            StringComparison.Ordinal);
        var blobMatches = string.Equals(
            priorState.BlobName.RequireValue(),
            proposedState.BlobName.RequireValue(),
            StringComparison.Ordinal);
        var contentMatches = string.Equals(
            priorState.Content.RequireValue(),
            proposedState.Content.RequireValue(),
            StringComparison.Ordinal);

        return containerMatches && blobMatches && contentMatches
            ? priorState.ETag
            : TF<string>.Unknown();
    }

    private sealed record BlobSnapshot(
        string ContainerName,
        string BlobName,
        string Content,
        string Id,
        string ETag);
}
