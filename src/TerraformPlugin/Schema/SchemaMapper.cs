using Tfplugin6;
using ProtocolSchema = Tfplugin6.Schema;

namespace TerraformPlugin.Schema;

internal static class SchemaMapper
{
    public static ProtocolSchema ToProtocolSchema(ComponentSchema componentSchema) =>
        new()
        {
            Version = componentSchema.Version,
            Block = ToProtocolBlock(componentSchema.Block, componentSchema.Version),
        };

    public static Tfplugin6.ResourceIdentitySchema ToProtocolIdentitySchema(IdentitySchema identitySchema)
    {
        var protocolSchema = new Tfplugin6.ResourceIdentitySchema
        {
            Version = identitySchema.Version,
        };

        protocolSchema.IdentityAttributes.AddRange(identitySchema.Attributes.Select(ToProtocolIdentityAttribute));
        return protocolSchema;
    }

    private static ProtocolSchema.Types.Block ToProtocolBlock(SchemaBlock block, long version) =>
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

    private static ProtocolSchema.Types.Attribute ToProtocolAttribute(SchemaAttribute attribute) =>
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

    private static Tfplugin6.ResourceIdentitySchema.Types.IdentityAttribute ToProtocolIdentityAttribute(ResourceIdentityAttribute attribute) =>
        new()
        {
            Name = attribute.Name,
            Type = Google.Protobuf.ByteString.CopyFrom(attribute.Type.ToTypeBytes()),
            RequiredForImport = attribute.RequiredForImport,
            OptionalForImport = attribute.OptionalForImport,
            Description = attribute.Description,
        };

    private static ProtocolSchema.Types.NestedBlock ToProtocolNestedBlock(SchemaNestedBlock nestedBlock) =>
        new()
        {
            TypeName = nestedBlock.TypeName,
            Block = ToProtocolBlock(nestedBlock.Block, 0),
            Nesting = nestedBlock.Nesting switch
            {
                SchemaNestingMode.Single => ProtocolSchema.Types.NestedBlock.Types.NestingMode.Single,
                SchemaNestingMode.List => ProtocolSchema.Types.NestedBlock.Types.NestingMode.List,
                SchemaNestingMode.Set => ProtocolSchema.Types.NestedBlock.Types.NestingMode.Set,
                SchemaNestingMode.Map => ProtocolSchema.Types.NestedBlock.Types.NestingMode.Map,
                SchemaNestingMode.Group => ProtocolSchema.Types.NestedBlock.Types.NestingMode.Group,
                _ => ProtocolSchema.Types.NestedBlock.Types.NestingMode.Invalid,
            },
            MinItems = (long)nestedBlock.MinItems,
            MaxItems = (long)nestedBlock.MaxItems,
        };

    private static StringKind ToProtocolStringKind(SchemaStringKind kind) =>
        kind == SchemaStringKind.Markdown
            ? StringKind.Markdown
            : StringKind.Plain;
}
