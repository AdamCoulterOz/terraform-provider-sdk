namespace TerraformPlugin.Schema;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class NestedBlockAttribute : Attribute
{
    public NestedBlockAttribute()
    {
    }

    public NestedBlockAttribute(string typeName)
    {
        TypeName = typeName;
    }

    public string? TypeName { get; init; }
    public SchemaNestingMode? Nesting { get; init; }
    public int MinItems { get; init; }
    public int MaxItems { get; init; }
}
