namespace Azure.ResourceIds;

internal sealed class ResourceProviderNode(string @namespace)
{
    public string Namespace { get; } = @namespace;
    public ResourceTypeNode Resource(string typeName) => new(this, [typeName]);
}

internal sealed class ResourceTypeNode(ResourceProviderNode provider, IReadOnlyList<string> typePath)
{
    public ResourceProviderNode Provider { get; } = provider;
    public IReadOnlyList<string> TypePath { get; } = typePath;
    public ResourceTypeNode Child(string typeName) => new(Provider, [.. TypePath, typeName]);

    public string FormatResourceId(params string[] names)
    {
        if (names.Length != TypePath.Count)
        {
            throw new InvalidOperationException(
                $"Resource type path '{string.Join("/", TypePath)}' requires {TypePath.Count} names but received {names.Length}.");
        }

        var segments = new List<string>(2 + (TypePath.Count * 2))
        {
            "providers",
            Provider.Namespace,
        };

        for (var index = 0; index < TypePath.Count; index++)
        {
            segments.Add(TypePath[index]);
            segments.Add(names[index]);
        }

        return "/" + string.Join('/', segments);
    }

    public bool TryParseResourceId(string resourceId, out string[] names)
    {
        var segments = resourceId
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length != 2 + (TypePath.Count * 2) ||
            !string.Equals(segments[0], "providers", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(segments[1], Provider.Namespace, StringComparison.Ordinal))
        {
            names = [];
            return false;
        }

        names = new string[TypePath.Count];

        for (var index = 0; index < TypePath.Count; index++)
        {
            var typeSegment = segments[2 + (index * 2)];

            if (!string.Equals(typeSegment, TypePath[index], StringComparison.Ordinal))
            {
                names = [];
                return false;
            }

            names[index] = segments[3 + (index * 2)];
        }

        return true;
    }
}
