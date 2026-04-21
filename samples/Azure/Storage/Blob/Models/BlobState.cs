namespace Azure.Storage.Blob.Models;

internal sealed record BlobState(
    string ContainerName,
    string BlobName,
    string Content,
    string Id,
    string ETag);
