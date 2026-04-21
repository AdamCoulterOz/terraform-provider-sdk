namespace TerraformPluginDotnet.Provider;

internal static class TerraformTypeNames
{
    public static string Compose(string providerTypeName, string componentName)
    {
        if (string.IsNullOrWhiteSpace(providerTypeName))
        {
            throw new InvalidOperationException("Provider type name must be a non-empty string.");
        }

        if (string.IsNullOrWhiteSpace(componentName))
        {
            throw new InvalidOperationException("Resource or data source name must be a non-empty string.");
        }

        return $"{providerTypeName}_{componentName}";
    }
}
