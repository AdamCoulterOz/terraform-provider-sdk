using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Provider;
using TerraformPluginDotnet.Schema;

namespace TerraformProviderFile;

internal sealed class FileProvider : TerraformProviderBase
{
    private static readonly TerraformComponentSchema ProviderComponentSchema =
        new(
            new TerraformSchemaBlock(
                new Dictionary<string, TerraformSchemaAttribute>(StringComparer.Ordinal)
                {
                    ["base_directory"] = new(
                        "base_directory",
                        TerraformPluginDotnet.Types.TerraformType.String,
                        Optional: true,
                        Description: "Base directory for relative file paths."),
                }));

    private readonly IReadOnlyDictionary<string, ITerraformResource> _resources =
        new Dictionary<string, ITerraformResource>(StringComparer.Ordinal)
        {
            ["file_managed"] = new FileManagedResource(),
        };

    private readonly IReadOnlyDictionary<string, ITerraformDataSource> _dataSources =
        new Dictionary<string, ITerraformDataSource>(StringComparer.Ordinal)
        {
            ["file_read"] = new FileReadDataSource(),
        };

    public override TerraformComponentSchema ProviderSchema => ProviderComponentSchema;

    public override IReadOnlyDictionary<string, ITerraformResource> Resources => _resources;

    public override IReadOnlyDictionary<string, ITerraformDataSource> DataSources => _dataSources;

    public override ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformProviderValidateRequest request, CancellationToken cancellationToken)
    {
        var baseDirectory = request.Config.GetOptionalString("base_directory");

        if (baseDirectory is not null && string.IsNullOrWhiteSpace(baseDirectory))
        {
            return ValueTask.FromResult(
                new TerraformValidateResult(
                    [
                        TerraformDiagnostic.Error(
                            "Invalid base directory",
                            "base_directory must be either omitted or a non-empty path.",
                            TerraformPluginDotnet.Types.TerraformAttributePath.Root("base_directory")),
                    ]));
        }

        return ValueTask.FromResult(TerraformValidateResult.Empty);
    }

    public override ValueTask<TerraformConfigureResult> ConfigureAsync(TerraformProviderConfigureRequest request, CancellationToken cancellationToken)
    {
        var configuredBaseDirectory = request.Config.GetOptionalString("base_directory");
        var baseDirectory = string.IsNullOrWhiteSpace(configuredBaseDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(configuredBaseDirectory);

        Directory.CreateDirectory(baseDirectory);

        return ValueTask.FromResult(new TerraformConfigureResult(new FileProviderState(baseDirectory)));
    }
}
