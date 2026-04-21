using TerraformPlugin.Types;

namespace TerraformPlugin.Schema;

public sealed record SchemaAttribute(
    string Name,
    TFType Type,
    bool Required = false,
    bool Optional = false,
    bool Computed = false,
    bool Sensitive = false,
    bool WriteOnly = false,
    string Description = "",
    SchemaStringKind DescriptionKind = SchemaStringKind.Plain,
    bool Deprecated = false,
    string DeprecationMessage = "");
