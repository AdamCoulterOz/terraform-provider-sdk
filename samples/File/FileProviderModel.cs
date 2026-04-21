using System.Security.Cryptography;
using System.Text;
using TerraformPlugin.Types;

namespace File;

internal sealed record FileProviderState(string BaseDirectory);

internal sealed record FileMaterializedState(
    string Path,
    string AbsolutePath,
    string Content,
    string Sha256);

internal static class FileProviderModel
{
    public static FileManagedResource ToResource(FileMaterializedState state) =>
        new()
        {
            Path = TF<string>.Known(state.Path),
            Content = TF<string>.Known(state.Content),
            AbsolutePath = TF<string>.Known(state.AbsolutePath),
            Sha256 = TF<string>.Known(state.Sha256),
            Id = TF<string>.Known(state.AbsolutePath),
        };

    public static FileManagedResource UnknownResourcePlannedState(TF<string> pathValue, TF<string> contentValue) =>
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
        var content = System.IO.File.ReadAllText(absolutePath);
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

    public static IReadOnlyList<TerraformPlugin.Diagnostics.Diagnostic> ValidatePathValue(TF<string> pathValue, string attributeName)
    {
        if (pathValue.IsUnknown || pathValue.IsNull)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(pathValue.RequireValue()))
        {
            return
            [
                TerraformPlugin.Diagnostics.Diagnostic.Error(
                    "Invalid path",
                    $"{attributeName} must be a non-empty string.",
                    AttributePath.Root(attributeName)),
            ];
        }

        return [];
    }
}
