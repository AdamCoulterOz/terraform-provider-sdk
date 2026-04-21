using TerraformPlugin.Types;

namespace TerraformPlugin.Schema;

internal static class ResourceIdentityConvention
{
    public static IdentitySchema? InferDefault(ComponentSchema resourceSchema)
    {
        if (!resourceSchema.Block.Attributes.TryGetValue("id", out var idAttribute))
            return null;

        if (!IsSupportedIdentityType(idAttribute.Type))
            return null;

        return new IdentitySchema(
            [
                new ResourceIdentityAttribute(
                    idAttribute.Name,
                    idAttribute.Type,
                    RequiredForImport: true,
                    OptionalForImport: false,
                    idAttribute.Description),
            ]);
    }

    private static bool IsSupportedIdentityType(TFType type) =>
        type switch
        {
            TerraformPrimitiveType => true,
            TFListType { ElementType: TerraformPrimitiveType } => true,
            _ => false,
        };
}
