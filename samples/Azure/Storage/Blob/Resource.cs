using Azure.Storage.Blob.Models;
using Azure.Storage.Blobs.Models;
using System.Diagnostics.CodeAnalysis;
using TerraformPluginDotnet;
using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

namespace Azure.Storage.Blob;

[method: SetsRequiredMembers]
internal sealed class Blob() : AzureResource<Blob>
{
    protected override AzureNode ParentNode => Storage.Instance;

    protected override string ResourceSegment => "blob";

    [TerraformAttribute(Description = "Blob container name.")]
    public required TF<string> ContainerName { get; init; }

    [TerraformAttribute(Description = "Blob name.")]
    public required TF<string> BlobName { get; init; }

    [TerraformAttribute(Description = "Blob content.")]
    public required TF<string> Content { get; init; }

    [TerraformAttribute(Computed = true, Description = "Azure resource identifier.")]
    public TF<string> Id { get; init; }

    [TerraformAttribute(Computed = true, Description = "Blob entity tag.")]
    public TF<string> ETag { get; init; }

    public override ValueTask<IReadOnlyList<TerraformDiagnostic>> ValidateConfigAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(ModelHelpers.ValidateNames(ContainerName, BlobName));

    public override async ValueTask<TerraformModelResult<Blob>> ReadAsync(
        TerraformResourceContext<ProviderState> context,
        CancellationToken cancellationToken)
    {
        if (ContainerName.IsNull || ContainerName.IsUnknown || BlobName.IsNull || BlobName.IsUnknown)
        {
            return new TerraformModelResult<Blob>(null, PrivateState: context.PrivateState);
        }

        var state = await ModelHelpers.ReadExistingAsync(
            context.ProviderState,
            ContainerName.RequireValue(),
            BlobName.RequireValue(),
            cancellationToken).ConfigureAwait(false);

        return new TerraformModelResult<Blob>(
            state is null ? null : ModelHelpers.ToResource(state),
            PrivateState: context.PrivateState);
    }

    public override ValueTask<TerraformPlanResult<Blob>> PlanAsync(
        Blob? priorState,
        TerraformResourceContext<ProviderState> context,
        CancellationToken cancellationToken)
    {
        if (ContainerName.IsUnknown || BlobName.IsUnknown || Content.IsUnknown)
        {
            return ValueTask.FromResult(
                new TerraformPlanResult<Blob>(
                    ModelHelpers.UnknownPlannedState(context.ProviderState, ContainerName, BlobName, Content),
                    PlannedPrivateState: context.PriorPrivateState));
        }

        var requiresReplace = ModelHelpers.GetReplacePaths(
            priorState,
            ContainerName.RequireValue(),
            BlobName.RequireValue());
        var etag = ModelHelpers.GetPlannedETag(priorState, this);

        return ValueTask.FromResult(
            new TerraformPlanResult<Blob>(
                new Blob
                {
                    ContainerName = ContainerName,
                    BlobName = BlobName,
                    Content = Content,
                    Id = TF<string>.Known(ModelHelpers.CreateBlobId(
                        context.ProviderState,
                        ContainerName.RequireValue(),
                        BlobName.RequireValue())),
                    ETag = etag,
                },
                PlannedPrivateState: context.PriorPrivateState,
                RequiresReplace: requiresReplace));
    }

    public override async ValueTask<TerraformModelResult<Blob>> ApplyAsync(
        Blob? priorState,
        TerraformResourceContext<ProviderState> context,
        CancellationToken cancellationToken)
    {
        var containerName = ContainerName.RequireValue();
        var blobName = BlobName.RequireValue();
        var content = Content.RequireValue();
        var containerClient = context.ProviderState.ServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await blobClient.UploadAsync(BinaryData.FromString(content), overwrite: true, cancellationToken).ConfigureAwait(false);

        var state = await ModelHelpers.ReadExistingAsync(context.ProviderState, containerName, blobName, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Azure Storage blob was not readable after upload.");

        return new TerraformModelResult<Blob>(
            ModelHelpers.ToResource(state),
            PrivateState: context.PlannedPrivateState);
    }

    public override async ValueTask<TerraformModelResult<Blob>> DeleteAsync(
        Blob? priorState,
        TerraformResourceContext<ProviderState> context,
        CancellationToken cancellationToken)
    {
        if (priorState is not null &&
            priorState.ContainerName.IsKnown &&
            priorState.BlobName.IsKnown)
        {
            var existingBlobClient = ModelHelpers.GetBlobClient(
                context.ProviderState,
                priorState.ContainerName.RequireValue(),
                priorState.BlobName.RequireValue());

            await existingBlobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return new TerraformModelResult<Blob>(null, PrivateState: context.PlannedPrivateState);
    }

    public override async ValueTask<TerraformImportResult<Blob>> ImportAsync(
        string id,
        TerraformResourceContext<ProviderState> context,
        CancellationToken cancellationToken)
    {
        if (!ModelHelpers.TryParseBlobId(id, out var accountName, out var containerName, out var blobName))
        {
            return new TerraformImportResult<Blob>(
                [],
                [
                    TerraformDiagnostic.Error(
                        "Invalid import id",
                        "Expected either an Azure-style resource id or the shorthand '<container>/<blob>'."),
                ]);
        }

        if (!string.IsNullOrEmpty(accountName) &&
            !string.Equals(accountName, context.ProviderState.AccountName, StringComparison.Ordinal))
        {
            return new TerraformImportResult<Blob>(
                [],
                [
                    TerraformDiagnostic.Error(
                        "Import target account mismatch",
                        $"The import id references storage account '{accountName}', but the configured provider is connected to '{context.ProviderState.AccountName}'."),
                ]);
        }

        var state = await ModelHelpers.ReadExistingAsync(context.ProviderState, containerName, blobName, cancellationToken).ConfigureAwait(false);

        if (state is null)
        {
            return new TerraformImportResult<Blob>(
                [],
                [
                    TerraformDiagnostic.Error(
                        "Import target not found",
                        $"No blob exists at '{id}'."),
                ]);
        }

        return new TerraformImportResult<Blob>([ModelHelpers.ToResource(state)]);
    }

    [DataSourceQuery]
    public static async ValueTask<Blob?> GetAsync(
        ProviderState providerState,
        TF<string> containerName,
        TF<string> blobName,
        CancellationToken cancellationToken)
    {
        var state = await ModelHelpers.ReadExistingAsync(
            providerState,
            containerName.RequireValue(),
            blobName.RequireValue(),
            cancellationToken).ConfigureAwait(false);

        return state is null ? null : ModelHelpers.ToResource(state);
    }
}
