namespace TerraformPlugin;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ResourceAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
