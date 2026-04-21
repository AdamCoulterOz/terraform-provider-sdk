namespace TerraformPlugin.Schema;

public sealed record SchemaNestedBlock(
    string TypeName,
    SchemaNestingMode Nesting,
    SchemaBlock Block,
    int MinItems = 0,
    int MaxItems = 0);
