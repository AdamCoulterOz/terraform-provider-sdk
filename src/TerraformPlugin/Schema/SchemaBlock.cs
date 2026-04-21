using TerraformPlugin.Types;

namespace TerraformPlugin.Schema;

public sealed record SchemaBlock(
    IReadOnlyDictionary<string, SchemaAttribute> Attributes,
    IReadOnlyDictionary<string, SchemaNestedBlock>? NestedBlocks = null,
    string Description = "",
    SchemaStringKind DescriptionKind = SchemaStringKind.Plain,
    bool Deprecated = false,
    string DeprecationMessage = "")
{
    public IReadOnlyDictionary<string, SchemaNestedBlock> NestedBlocks { get; init; } =
        NestedBlocks ?? new Dictionary<string, SchemaNestedBlock>(StringComparer.Ordinal);

    public TerraformObjectType ValueType()
    {
        var types = new Dictionary<string, TFType>(StringComparer.Ordinal);

        foreach (var attribute in Attributes.Values)
        {
            types[attribute.Name] = attribute.Type;
        }

        foreach (var block in NestedBlocks.Values)
        {
            var blockObjectType = block.Block.ValueType();
            types[block.TypeName] = block.Nesting switch
            {
                SchemaNestingMode.Single or SchemaNestingMode.Group => blockObjectType,
                SchemaNestingMode.List => new TFListType(blockObjectType),
                SchemaNestingMode.Set => new TFSetType(blockObjectType),
                SchemaNestingMode.Map => new TFMapType(blockObjectType),
                _ => throw new InvalidOperationException($"Unsupported nested block nesting mode '{block.Nesting}'."),
            };
        }

        return new TerraformObjectType(types);
    }
}
