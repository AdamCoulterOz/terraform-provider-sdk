using TerraformPlugin;
using Azure.ResourceIds;
using System.Reflection;

namespace Azure;

internal interface IAzureProvider<TSelf>
    where TSelf : AzureProvider
{
    static abstract TSelf Instance { get; }
}

internal abstract class AzureProvider
{
    public abstract string ResourceProviderNamespace { get; }
    public abstract string Name { get; }
}

internal abstract class AzureResource<TSelf, TProvider> : Resource<TSelf, ProviderState>
    where TSelf : AzureResource<TSelf, TProvider>
    where TProvider : AzureProvider, IAzureProvider<TProvider>
{
    protected TProvider Provider => TProvider.Instance;
    protected ResourceTypeNode ResourceType => ResolveResourceType();

    public sealed override string Name
    {
        get
        {
            var resourceName = ResolveResourceName(typeof(TSelf));
            return string.Equals(resourceName, Provider.Name, StringComparison.Ordinal)
                ? Provider.Name
                : $"{Provider.Name}_{resourceName}";
        }
    }

    protected string FormatResourceId(params string[] names) => ResourceType.FormatResourceId(names);

    protected bool TryParseResourceId(string resourceId, out string[] names) =>
        ResourceType.TryParseResourceId(resourceId, out names);

    protected bool TryParseLeafResourceName(string resourceId, out string name)
    {
        if (TryParseResourceId(resourceId, out var names) && names.Length > 0)
        {
            name = names[^1];
            return true;
        }

        name = string.Empty;
        return false;
    }

    private static ResourceTypeNode ResolveResourceType()
    {
        var property = typeof(TProvider)
            .GetProperty(typeof(TSelf).Name, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException(
                $"Azure provider '{typeof(TProvider).Name}' does not declare a ResourceTypeNode property named '{typeof(TSelf).Name}'.");

        if (property.PropertyType != typeof(ResourceTypeNode))
        {
            throw new InvalidOperationException(
                $"Azure provider property '{typeof(TProvider).Name}.{property.Name}' must be a {nameof(ResourceTypeNode)}.");
        }

        return (ResourceTypeNode)(property.GetValue(TProvider.Instance)
            ?? throw new InvalidOperationException(
                $"Azure provider property '{typeof(TProvider).Name}.{property.Name}' returned null."));
    }
}
