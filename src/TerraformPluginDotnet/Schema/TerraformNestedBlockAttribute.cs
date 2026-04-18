namespace TerraformPluginDotnet.Schema;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class TerraformNestedBlockAttribute : Attribute
{
    public TerraformNestedBlockAttribute()
    {
    }

    public TerraformNestedBlockAttribute(string typeName)
    {
        TypeName = typeName;
    }

    public string? TypeName { get; init; }
    public TerraformSchemaNestingMode? Nesting { get; init; }
    public int MinItems { get; init; }
    public int MaxItems { get; init; }
}
