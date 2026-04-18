using TerraformPluginDotnet;
using TerraformPluginDotnet.Types;

namespace TerraformProviderFile;

internal sealed class FileReadDataSource : TerraformDataSource<FileReadDataSourceModel, FileProviderState>
{
    public override string TypeName => "file_read";

    public override ValueTask<IReadOnlyList<TerraformPluginDotnet.Diagnostics.TerraformDiagnostic>> ValidateConfigAsync(FileReadDataSourceModel request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(FileProviderModel.ValidatePathValue(request.Path, "path"));

    public override ValueTask<TerraformModelResult<FileReadDataSourceModel>> ReadAsync(
        FileReadDataSourceModel request,
        TerraformDataSourceContext<FileProviderState> context,
        CancellationToken cancellationToken)
    {
        var providerState = context.ProviderState;
        var path = request.Path.RequireValue();
        var absolutePath = FileProviderModel.ResolvePath(providerState, path);

        if (!File.Exists(absolutePath))
        {
            return ValueTask.FromResult(
                new TerraformModelResult<FileReadDataSourceModel>(
                    null,
                    Diagnostics:
                    [
                        TerraformPluginDotnet.Diagnostics.TerraformDiagnostic.Error(
                            "File not found",
                            $"No file exists at '{absolutePath}'."),
                    ]));
        }

        var materialized = FileProviderModel.ReadExisting(providerState, path);
        return ValueTask.FromResult(new TerraformModelResult<FileReadDataSourceModel>(FileProviderModel.ToDataSourceModel(materialized)));
    }
}
