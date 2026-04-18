using TerraformPluginDotnet;
using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

namespace TerraformProviderFile;

internal sealed class FileProvider : TerraformProvider<FileProviderConfigModel, FileProviderState>
{
    public override IEnumerable<TerraformResource<FileProviderState>> Resources =>
        [new FileManagedResource()];

    public override IEnumerable<TerraformDataSource<FileProviderState>> DataSources =>
        [new FileReadDataSource()];

    public override ValueTask<IReadOnlyList<TerraformDiagnostic>> ValidateConfigAsync(FileProviderConfigModel request, CancellationToken cancellationToken)
    {
        var baseDirectory = request.BaseDirectory.GetValueOrDefault();

        if (baseDirectory is not null && string.IsNullOrWhiteSpace(baseDirectory))
        {
            return ValueTask.FromResult(
                (IReadOnlyList<TerraformDiagnostic>)
                [
                    TerraformDiagnostic.Error(
                        "Invalid base directory",
                        "base_directory must be either omitted or a non-empty path.",
                        TerraformAttributePath.Root("base_directory")),
                ]);
        }

        return ValueTask.FromResult<IReadOnlyList<TerraformDiagnostic>>([]);
    }

    public override ValueTask<FileProviderState> ConfigureAsync(FileProviderConfigModel request, TerraformProviderContext context, CancellationToken cancellationToken)
    {
        var configuredBaseDirectory = request.BaseDirectory.GetValueOrDefault();
        var baseDirectory = string.IsNullOrWhiteSpace(configuredBaseDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(configuredBaseDirectory);

        Directory.CreateDirectory(baseDirectory);

        return ValueTask.FromResult(new FileProviderState(baseDirectory));
    }
}

internal sealed class FileProviderConfigModel
{
    [TerraformAttribute(Optional = true, Description = "Base directory for relative file paths.")]
    public TF<string> BaseDirectory { get; init; }
}
