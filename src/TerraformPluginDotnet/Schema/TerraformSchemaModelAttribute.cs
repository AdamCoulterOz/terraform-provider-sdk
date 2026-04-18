namespace TerraformPluginDotnet.Schema;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class TerraformSchemaModelAttribute : Attribute
{
    public long Version { get; init; }
    public string Description { get; init; } = string.Empty;
    public TerraformSchemaStringKind DescriptionKind { get; init; } = TerraformSchemaStringKind.Plain;
    public bool Deprecated { get; init; }
    public string DeprecationMessage { get; init; } = string.Empty;
}
