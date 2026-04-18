using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Schema;

public sealed record TerraformSchemaBlock(
    IReadOnlyDictionary<string, TerraformSchemaAttribute> Attributes,
    IReadOnlyDictionary<string, TerraformSchemaNestedBlock>? NestedBlocks = null,
    string Description = "",
    TerraformSchemaStringKind DescriptionKind = TerraformSchemaStringKind.Plain,
    bool Deprecated = false,
    string DeprecationMessage = "")
{
    public IReadOnlyDictionary<string, TerraformSchemaNestedBlock> NestedBlocks { get; init; } =
        NestedBlocks ?? new Dictionary<string, TerraformSchemaNestedBlock>(StringComparer.Ordinal);

    public TerraformObjectType ValueType()
    {
        var types = new Dictionary<string, TerraformType>(StringComparer.Ordinal);

        foreach (var attribute in Attributes.Values)
        {
            types[attribute.Name] = attribute.Type;
        }

        foreach (var block in NestedBlocks.Values)
        {
            var blockObjectType = block.Block.ValueType();
            types[block.TypeName] = block.Nesting switch
            {
                TerraformSchemaNestingMode.Single or TerraformSchemaNestingMode.Group => blockObjectType,
                TerraformSchemaNestingMode.List => new TerraformListType(blockObjectType),
                TerraformSchemaNestingMode.Set => new TerraformSetType(blockObjectType),
                TerraformSchemaNestingMode.Map => new TerraformMapType(blockObjectType),
                _ => throw new InvalidOperationException($"Unsupported nested block nesting mode '{block.Nesting}'."),
            };
        }

        return new TerraformObjectType(types);
    }
}
