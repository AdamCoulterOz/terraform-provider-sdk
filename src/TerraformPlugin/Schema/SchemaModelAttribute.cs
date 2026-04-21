namespace TerraformPlugin.Schema;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class SchemaModelAttribute : Attribute
{
    public long Version { get; init; }
    public string Description { get; init; } = string.Empty;
    public SchemaStringKind DescriptionKind { get; init; } = SchemaStringKind.Plain;
    public bool Deprecated { get; init; }
    public string DeprecationMessage { get; init; } = string.Empty;
}
