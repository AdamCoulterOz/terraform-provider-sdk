using Azure.ResourceIds;

namespace Azure.Storage;

internal sealed class Storage : AzureProvider, IAzureProvider<Storage>
{
    private const string NamespaceName = "Microsoft.Storage";
    public static Storage Instance { get; } = new();

    private readonly ResourceProviderNode _root = new(NamespaceName);
    public ResourceTypeNode Account => _root.Resource("storageAccounts");
    public ResourceTypeNode BlobService => Account.Child("blobServices");
    public ResourceTypeNode Container => BlobService.Child("containers");
    public ResourceTypeNode Blob => Container.Child("blobs");

    public override string ResourceProviderNamespace => "Microsoft.Storage";
    public override string Name => "storage";
}
