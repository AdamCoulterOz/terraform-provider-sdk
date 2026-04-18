using System.Security.Cryptography;
using System.Text;
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
    public static FileManagedResourceModel ToResourceModel(FileMaterializedState state) =>
        new()
        {
            Path = TF<string>.Known(state.Path),
            Content = TF<string>.Known(state.Content),
            AbsolutePath = TF<string>.Known(state.AbsolutePath),
            Sha256 = TF<string>.Known(state.Sha256),
            Id = TF<string>.Known(state.AbsolutePath),
        };

    public static FileReadDataSourceModel ToDataSourceModel(FileMaterializedState state) =>
        new()
        {
            Path = TF<string>.Known(state.Path),
            Content = TF<string>.Known(state.Content),
            AbsolutePath = TF<string>.Known(state.AbsolutePath),
            Sha256 = TF<string>.Known(state.Sha256),
        };

    public static FileManagedResourceModel UnknownResourcePlannedState(TF<string> pathValue, TF<string> contentValue) =>
        new()
        {
            Path = pathValue,
            Content = contentValue,
            AbsolutePath = TF<string>.Unknown(),
            Sha256 = TF<string>.Unknown(),
            Id = TF<string>.Unknown(),
        };

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

    public static IReadOnlyList<TerraformPluginDotnet.Diagnostics.TerraformDiagnostic> ValidatePathValue(TF<string> pathValue, string attributeName)
    {
        if (pathValue.IsUnknown || pathValue.IsNull)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(pathValue.RequireValue()))
        {
            return
            [
                TerraformPluginDotnet.Diagnostics.TerraformDiagnostic.Error(
                    "Invalid path",
                    $"{attributeName} must be a non-empty string.",
                    TerraformAttributePath.Root(attributeName)),
            ];
        }

        return [];
    }
}

internal sealed class FileManagedResourceModel
{
    [TerraformAttribute(Description = "Path to the managed file.")]
    public TF<string> Path { get; init; }

    [TerraformAttribute(Description = "Desired file content.")]
    public TF<string> Content { get; init; }

    [TerraformAttribute(Computed = true, Description = "Canonical absolute path.")]
    public TF<string> AbsolutePath { get; init; }

    [TerraformAttribute(Computed = true, Description = "SHA-256 of the file content.")]
    public TF<string> Sha256 { get; init; }

    [TerraformAttribute(Computed = true, Description = "Provider-assigned resource identifier.")]
    public TF<string> Id { get; init; }
}

internal sealed class FileReadDataSourceModel
{
    [TerraformAttribute(Description = "Path to the file to read.")]
    public TF<string> Path { get; init; }

    [TerraformAttribute(Computed = true, Description = "Current file content.")]
    public TF<string> Content { get; init; }

    [TerraformAttribute(Computed = true, Description = "Canonical absolute path.")]
    public TF<string> AbsolutePath { get; init; }

    [TerraformAttribute(Computed = true, Description = "SHA-256 of the file content.")]
    public TF<string> Sha256 { get; init; }
}
