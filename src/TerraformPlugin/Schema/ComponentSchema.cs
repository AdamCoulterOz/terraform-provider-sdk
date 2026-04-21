namespace TerraformPlugin.Schema;

public sealed record ComponentSchema(
    SchemaBlock Block,
    long Version = 0);
