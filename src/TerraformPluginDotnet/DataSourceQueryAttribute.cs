namespace TerraformPluginDotnet;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DataSourceQueryAttribute : Attribute
{
    public string? Name { get; init; }

    public string ItemsName { get; init; } = "items";
}
