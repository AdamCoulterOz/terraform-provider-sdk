namespace TerraformPluginDotnet.Schema;

public sealed record TerraformComponentSchema(
    TerraformSchemaBlock Block,
    long Version = 0);
