using TerraformPlugin;
using TerraformPlugin.Schema;
using TerraformPlugin.Types;
using TerraformPlugin.Validation;

namespace File;

internal sealed class FileProvider : Provider<FileProviderConfigModel, FileProviderState>
{
    public override string TypeName => "file";

    public override IEnumerable<Resource<FileProviderState>> Resources =>
        [Resource<FileManagedResource>()];

    public override IEnumerable<DataSource<FileProviderState>> DataSources =>
        [];

    public override ValueTask<FileProviderState> ConfigureAsync(FileProviderConfigModel request, ProviderContext context, CancellationToken cancellationToken)
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
    [TFAttribute(Optional = true, Description = "Base directory for relative file paths.")]
    [NotEmpty]
    public TF<string> BaseDirectory { get; init; }
}
