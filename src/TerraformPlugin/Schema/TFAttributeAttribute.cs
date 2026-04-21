namespace TerraformPlugin.Schema;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class TFAttributeAttribute : Attribute
{
    public TFAttributeAttribute()
    {
    }

    public TFAttributeAttribute(string name)
    {
        Name = name;
    }

    public string? Name { get; init; }
    public bool Required { get; init; }
    public bool Optional { get; init; }
    public bool Computed { get; init; }
    public bool Sensitive { get; init; }
    public bool WriteOnly { get; init; }
    public string Description { get; init; } = string.Empty;
    public SchemaStringKind DescriptionKind { get; init; } = SchemaStringKind.Plain;
    public bool Deprecated { get; init; }
    public string DeprecationMessage { get; init; } = string.Empty;
}
