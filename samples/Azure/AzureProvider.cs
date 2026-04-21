using TerraformPluginDotnet;

namespace Azure;

internal abstract class AzureNode
{
    protected abstract string Segment { get; }

    protected virtual AzureNode? Parent => null;

    public string Name => Parent is null
        ? TerraformName.ToSnakeCase(Segment)
        : $"{Parent.Name}_{TerraformName.ToSnakeCase(Segment)}";
}

internal abstract class AzureProvider(string resourceProviderNamespace) : AzureNode
{
    public string ResourceProviderNamespace { get; } = resourceProviderNamespace;
}

internal abstract class AzureService : AzureNode;

internal abstract class AzureResource<TSelf> : TerraformResource<TSelf, ProviderState>
    where TSelf : AzureResource<TSelf>, new()
{
    protected abstract AzureNode ParentNode { get; }

    protected abstract string ResourceSegment { get; }

    public sealed override string Name => $"{ParentNode.Name}_{TerraformName.ToSnakeCase(ResourceSegment)}";
}

internal static class TerraformName
{
    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new System.Text.StringBuilder(name.Length + 8);

        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];

            if (char.IsUpper(current))
            {
                if (index > 0)
                {
                    var previous = name[index - 1];
                    var nextIsLower = index + 1 < name.Length && char.IsLower(name[index + 1]);

                    if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                    {
                        builder.Append('_');
                    }
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
