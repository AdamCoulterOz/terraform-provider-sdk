namespace TerraformPluginDotnet.Schema;

public sealed record TerraformSchemaNestedBlock(
    string TypeName,
    TerraformSchemaNestingMode Nesting,
    TerraformSchemaBlock Block,
    int MinItems = 0,
    int MaxItems = 0);
