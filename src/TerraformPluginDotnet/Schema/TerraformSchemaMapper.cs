using Tfplugin6;
using ProtocolSchema = Tfplugin6.Schema;

namespace TerraformPluginDotnet.Schema;

internal static class TerraformSchemaMapper
{
    public static ProtocolSchema ToProtocolSchema(TerraformComponentSchema componentSchema) =>
        new()
        {
            Version = componentSchema.Version,
            Block = ToProtocolBlock(componentSchema.Block, componentSchema.Version),
        };

    private static ProtocolSchema.Types.Block ToProtocolBlock(TerraformSchemaBlock block, long version) =>
        new()
        {
            Version = version,
            Description = block.Description,
            DescriptionKind = ToProtocolStringKind(block.DescriptionKind),
            Deprecated = block.Deprecated,
            DeprecationMessage = block.DeprecationMessage,
            Attributes = { block.Attributes.Values.Select(ToProtocolAttribute) },
            BlockTypes = { block.NestedBlocks.Values.Select(ToProtocolNestedBlock) },
        };

    private static ProtocolSchema.Types.Attribute ToProtocolAttribute(TerraformSchemaAttribute attribute) =>
        new()
        {
            Name = attribute.Name,
            Type = Google.Protobuf.ByteString.CopyFrom(attribute.Type.ToTypeBytes()),
            Description = attribute.Description,
            Required = attribute.Required,
            Optional = attribute.Optional,
            Computed = attribute.Computed,
            Sensitive = attribute.Sensitive,
            DescriptionKind = ToProtocolStringKind(attribute.DescriptionKind),
            Deprecated = attribute.Deprecated,
            WriteOnly = attribute.WriteOnly,
            DeprecationMessage = attribute.DeprecationMessage,
        };

    private static ProtocolSchema.Types.NestedBlock ToProtocolNestedBlock(TerraformSchemaNestedBlock nestedBlock) =>
        new()
        {
            TypeName = nestedBlock.TypeName,
            Block = ToProtocolBlock(nestedBlock.Block, 0),
            Nesting = nestedBlock.Nesting switch
            {
                TerraformSchemaNestingMode.Single => ProtocolSchema.Types.NestedBlock.Types.NestingMode.Single,
                TerraformSchemaNestingMode.List => ProtocolSchema.Types.NestedBlock.Types.NestingMode.List,
                TerraformSchemaNestingMode.Set => ProtocolSchema.Types.NestedBlock.Types.NestingMode.Set,
                TerraformSchemaNestingMode.Map => ProtocolSchema.Types.NestedBlock.Types.NestingMode.Map,
                TerraformSchemaNestingMode.Group => ProtocolSchema.Types.NestedBlock.Types.NestingMode.Group,
                _ => ProtocolSchema.Types.NestedBlock.Types.NestingMode.Invalid,
            },
            MinItems = (long)nestedBlock.MinItems,
            MaxItems = (long)nestedBlock.MaxItems,
        };

    private static StringKind ToProtocolStringKind(TerraformSchemaStringKind kind) =>
        kind == TerraformSchemaStringKind.Markdown
            ? StringKind.Markdown
            : StringKind.Plain;
}
