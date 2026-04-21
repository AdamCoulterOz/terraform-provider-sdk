using Azure.ResourceIds;

namespace Azure.Storage;

internal sealed class Storage : AzureProvider
{
    public new const string ResourceProviderNamespace = "Microsoft.Storage";

    public static Storage Instance { get; } = new();

    private Storage() : base(ResourceProviderNamespace) { }

    protected override string Segment => "storage";

    private static readonly ResourceProviderNode Root = new(ResourceProviderNamespace);

    public static ResourceTypeNode StorageAccount { get; } = Root.Resource("storageAccounts");

    public static ResourceTypeNode BlobService { get; } = StorageAccount.Child("blobServices");

    public static ResourceTypeNode Container { get; } = BlobService.Child("containers");

    public static ResourceTypeNode Blob { get; } = Container.Child("blobs");
}
