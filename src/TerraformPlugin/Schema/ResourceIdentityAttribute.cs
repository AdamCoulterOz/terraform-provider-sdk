using TerraformPlugin.Types;

namespace TerraformPlugin.Schema;

internal sealed record ResourceIdentityAttribute(
    string Name,
    TFType Type,
    bool RequiredForImport,
    bool OptionalForImport,
    string Description = "");

internal sealed record IdentitySchema(
    IReadOnlyList<ResourceIdentityAttribute> Attributes,
    long Version = 0)
{
    public TerraformObjectType ValueType() =>
        new(
            Attributes.ToDictionary(
                static attribute => attribute.Name,
                static attribute => attribute.Type,
                StringComparer.Ordinal));
}
