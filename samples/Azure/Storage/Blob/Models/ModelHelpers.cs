using Azure.Storage.Blobs;
using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Types;
using BlobResource = Azure.Storage.Blob.Blob;
using StorageProvider = Azure.Storage.Storage;

namespace Azure.Storage.Blob.Models;

internal static class ModelHelpers
{
    public static string CreateBlobId(ProviderState providerState, string containerName, string blobName) =>
        StorageProvider.Blob.FormatResourceId(
            providerState.AccountName,
            "default",
            containerName,
            blobName);

    public static bool TryParseBlobId(string id, out string accountName, out string containerName, out string blobName)
    {
        if (StorageProvider.Blob.TryParseResourceId(id, out var names))
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

    public static BlobClient GetBlobClient(ProviderState providerState, string containerName, string blobName) =>
        providerState.ServiceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobName);

    public static async Task<BlobState?> ReadExistingAsync(
        ProviderState providerState,
        string containerName,
        string blobName,
        CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(providerState, containerName, blobName);

        if (!await blobClient.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var download = await blobClient.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return new BlobState(
            containerName,
            blobName,
            download.Value.Content.ToString(),
            CreateBlobId(providerState, containerName, blobName),
            properties.Value.ETag.ToString());
    }

    public static BlobResource ToResource(BlobState state) =>
        new()
        {
            ContainerName = TF<string>.Known(state.ContainerName),
            BlobName = TF<string>.Known(state.BlobName),
            Content = TF<string>.Known(state.Content),
            Id = TF<string>.Known(state.Id),
            ETag = TF<string>.Known(state.ETag),
        };

    public static BlobResource UnknownPlannedState(ProviderState providerState, TF<string> containerName, TF<string> blobName, TF<string> content) =>
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

    public static IReadOnlyList<TerraformDiagnostic> ValidateNames(TF<string> containerName, TF<string> blobName)
    {
        var diagnostics = new List<TerraformDiagnostic>();

        if (containerName.IsKnown && string.IsNullOrWhiteSpace(containerName.RequireValue()))
        {
            diagnostics.Add(TerraformDiagnostic.Error(
                "Invalid container_name",
                "container_name must be a non-empty string.",
                TerraformAttributePath.Root("container_name")));
        }

        if (blobName.IsKnown && string.IsNullOrWhiteSpace(blobName.RequireValue()))
        {
            diagnostics.Add(TerraformDiagnostic.Error(
                "Invalid blob_name",
                "blob_name must be a non-empty string.",
                TerraformAttributePath.Root("blob_name")));
        }

        return diagnostics;
    }

    public static IReadOnlyList<TerraformAttributePath>? GetReplacePaths(BlobResource? priorState, string nextContainerName, string nextBlobName)
    {
        if (priorState is null || priorState.ContainerName.IsUnknown || priorState.BlobName.IsUnknown || priorState.ContainerName.IsNull || priorState.BlobName.IsNull)
        {
            return null;
        }

        var requiresReplace = new List<TerraformAttributePath>();

        if (!string.Equals(priorState.ContainerName.RequireValue(), nextContainerName, StringComparison.Ordinal))
        {
            requiresReplace.Add(TerraformAttributePath.Root("container_name"));
        }

        if (!string.Equals(priorState.BlobName.RequireValue(), nextBlobName, StringComparison.Ordinal))
        {
            requiresReplace.Add(TerraformAttributePath.Root("blob_name"));
        }

        return requiresReplace.Count == 0 ? null : requiresReplace;
    }

    public static TF<string> GetPlannedETag(BlobResource? priorState, BlobResource proposedState)
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
        {
            return TF<string>.Unknown();
        }

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
}
