using TerraformPluginDotnet.Provider;
using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

namespace TerraformProviderFile;

internal sealed class FileReadDataSource : TerraformDataSourceBase
{
    public override TerraformComponentSchema Schema => FileProviderModel.DataSourceSchema;

    public override ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformDataSourceValidateRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(FileProviderModel.ValidatePathValue(request.Config, "path"));

    public override ValueTask<TerraformReadResult> ReadAsync(TerraformDataSourceReadRequest request, CancellationToken cancellationToken)
    {
        var providerState = FileProviderModel.RequireProviderState(request.ProviderState);
        var path = request.Config.GetAttribute("path").AsString();
        var absolutePath = FileProviderModel.ResolvePath(providerState, path);

        if (!File.Exists(absolutePath))
        {
            return ValueTask.FromResult(
                new TerraformReadResult(
                    TerraformValue.Null(Schema.Block.ValueType()),
                    Diagnostics:
                    [
                        TerraformPluginDotnet.Diagnostics.TerraformDiagnostic.Error(
                            "File not found",
                            $"No file exists at '{absolutePath}'."),
                    ]));
        }

        var materialized = FileProviderModel.ReadExisting(providerState, path);
        return ValueTask.FromResult(new TerraformReadResult(FileProviderModel.ToDataSourceValue(materialized)));
    }
}
