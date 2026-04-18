using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Schema;

public sealed record TerraformSchemaAttribute(
    string Name,
    TerraformType Type,
    bool Required = false,
    bool Optional = false,
    bool Computed = false,
    bool Sensitive = false,
    bool WriteOnly = false,
    string Description = "",
    TerraformSchemaStringKind DescriptionKind = TerraformSchemaStringKind.Plain,
    bool Deprecated = false,
    string DeprecationMessage = "");
