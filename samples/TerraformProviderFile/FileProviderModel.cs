using System.Security.Cryptography;
using System.Text;
using TerraformPluginDotnet.Provider;
using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

namespace TerraformProviderFile;

internal sealed record FileProviderState(string BaseDirectory);

internal sealed record FileMaterializedState(
    string Path,
    string AbsolutePath,
    string Content,
    string Sha256);

internal static class FileProviderModel
{
    public static readonly TerraformComponentSchema ResourceSchema =
        new(
            new TerraformSchemaBlock(
                new Dictionary<string, TerraformSchemaAttribute>(StringComparer.Ordinal)
                {
                    ["path"] = new("path", TerraformType.String, Required: true, Description: "Path to the managed file."),
                    ["content"] = new("content", TerraformType.String, Required: true, Description: "Desired file content."),
                    ["absolute_path"] = new("absolute_path", TerraformType.String, Computed: true, Description: "Canonical absolute path."),
                    ["sha256"] = new("sha256", TerraformType.String, Computed: true, Description: "SHA-256 of the file content."),
                    ["id"] = new("id", TerraformType.String, Computed: true, Description: "Provider-assigned resource identifier."),
                }));

    public static readonly TerraformComponentSchema DataSourceSchema =
        new(
            new TerraformSchemaBlock(
                new Dictionary<string, TerraformSchemaAttribute>(StringComparer.Ordinal)
                {
                    ["path"] = new("path", TerraformType.String, Required: true, Description: "Path to the file to read."),
                    ["content"] = new("content", TerraformType.String, Computed: true, Description: "Current file content."),
                    ["absolute_path"] = new("absolute_path", TerraformType.String, Computed: true, Description: "Canonical absolute path."),
                    ["sha256"] = new("sha256", TerraformType.String, Computed: true, Description: "SHA-256 of the file content."),
                }));

    private static readonly TerraformObjectType ResourceObjectType = ResourceSchema.Block.ValueType();
    private static readonly TerraformObjectType DataSourceObjectType = DataSourceSchema.Block.ValueType();

    public static FileProviderState RequireProviderState(object? providerState) =>
        providerState as FileProviderState
        ?? throw new InvalidOperationException("The file provider has not been configured.");

    public static TerraformValue NullResourceState() => TerraformValue.Null(ResourceObjectType);

    public static TerraformValue ToResourceValue(FileMaterializedState state) =>
        TerraformValue.Object(
            ResourceObjectType,
            new Dictionary<string, TerraformValue>(StringComparer.Ordinal)
            {
                ["path"] = TerraformValue.String(state.Path),
                ["content"] = TerraformValue.String(state.Content),
                ["absolute_path"] = TerraformValue.String(state.AbsolutePath),
                ["sha256"] = TerraformValue.String(state.Sha256),
                ["id"] = TerraformValue.String(state.AbsolutePath),
            });

    public static TerraformValue ToDataSourceValue(FileMaterializedState state) =>
        TerraformValue.Object(
            DataSourceObjectType,
            new Dictionary<string, TerraformValue>(StringComparer.Ordinal)
            {
                ["path"] = TerraformValue.String(state.Path),
                ["content"] = TerraformValue.String(state.Content),
                ["absolute_path"] = TerraformValue.String(state.AbsolutePath),
                ["sha256"] = TerraformValue.String(state.Sha256),
            });

    public static TerraformValue UnknownResourcePlannedState(TerraformValue pathValue, TerraformValue contentValue) =>
        TerraformValue.Object(
            ResourceObjectType,
            new Dictionary<string, TerraformValue>(StringComparer.Ordinal)
            {
                ["path"] = pathValue,
                ["content"] = contentValue,
                ["absolute_path"] = TerraformValue.Unknown(TerraformType.String),
                ["sha256"] = TerraformValue.Unknown(TerraformType.String),
                ["id"] = TerraformValue.Unknown(TerraformType.String),
            });

    public static FileMaterializedState Materialize(FileProviderState providerState, string path, string content)
    {
        var absolutePath = ResolvePath(providerState, path);
        var sha256 = ComputeSha256(content);
        return new FileMaterializedState(path, absolutePath, content, sha256);
    }

    public static FileMaterializedState ReadExisting(FileProviderState providerState, string path)
    {
        var absolutePath = ResolvePath(providerState, path);
        var content = File.ReadAllText(absolutePath);
        return new FileMaterializedState(path, absolutePath, content, ComputeSha256(content));
    }

    public static string ResolvePath(FileProviderState providerState, string path) =>
        Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(providerState.BaseDirectory, path));

    public static string ComputeSha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static TerraformValidateResult ValidatePathValue(TerraformValue config, string attributeName)
    {
        var pathValue = config.GetAttribute(attributeName);

        if (pathValue.IsUnknown || pathValue.IsNull)
        {
            return TerraformValidateResult.Empty;
        }

        if (string.IsNullOrWhiteSpace(pathValue.AsString()))
        {
            return new TerraformValidateResult(
                [
                    TerraformPluginDotnet.Diagnostics.TerraformDiagnostic.Error(
                        "Invalid path",
                        $"{attributeName} must be a non-empty string.",
                        TerraformAttributePath.Root(attributeName)),
                ]);
        }

        return TerraformValidateResult.Empty;
    }
}
