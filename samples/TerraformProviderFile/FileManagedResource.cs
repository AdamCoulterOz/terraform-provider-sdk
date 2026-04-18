using TerraformPluginDotnet;
using TerraformPluginDotnet.Types;

namespace TerraformProviderFile;

internal sealed class FileManagedResource : TerraformResource<FileManagedResourceModel, FileProviderState>
{
    public override string TypeName => "file_managed";

    public override ValueTask<IReadOnlyList<TerraformPluginDotnet.Diagnostics.TerraformDiagnostic>> ValidateConfigAsync(FileManagedResourceModel request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(FileProviderModel.ValidatePathValue(request.Path, "path"));

    public override ValueTask<TerraformModelResult<FileManagedResourceModel>> ReadAsync(
        FileManagedResourceModel? request,
        TerraformResourceContext<FileProviderState> context,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ValueTask.FromResult(new TerraformModelResult<FileManagedResourceModel>(null));
        }

        var providerState = context.ProviderState;
        var pathValue = request.Path;

        if (pathValue.IsUnknown || pathValue.IsNull)
        {
            return ValueTask.FromResult(new TerraformModelResult<FileManagedResourceModel>(null));
        }

        var path = pathValue.RequireValue();
        var absolutePath = FileProviderModel.ResolvePath(providerState, path);

        if (!File.Exists(absolutePath))
        {
            return ValueTask.FromResult(new TerraformModelResult<FileManagedResourceModel>(null));
        }

        return ValueTask.FromResult(
            new TerraformModelResult<FileManagedResourceModel>(
                FileProviderModel.ToResourceModel(FileProviderModel.ReadExisting(providerState, path))));
    }

    public override ValueTask<TerraformPlanResult<FileManagedResourceModel>> PlanAsync(
        FileManagedResourceModel? priorState,
        FileManagedResourceModel? request,
        TerraformResourceContext<FileProviderState> context,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new InvalidOperationException("Planned resource state was not available.");
        }

        var pathValue = request.Path;
        var contentValue = request.Content;

        if (pathValue.IsUnknown || contentValue.IsUnknown)
        {
            return ValueTask.FromResult(
                new TerraformPlanResult<FileManagedResourceModel>(
                    FileProviderModel.UnknownResourcePlannedState(pathValue, contentValue)));
        }

        var providerState = context.ProviderState;
        var materialized = FileProviderModel.Materialize(providerState, pathValue.RequireValue(), contentValue.RequireValue());
        var requiresReplace = GetReplacePathsIfNeeded(providerState, priorState, materialized.AbsolutePath);

        return ValueTask.FromResult(
            new TerraformPlanResult<FileManagedResourceModel>(
                FileProviderModel.ToResourceModel(materialized),
                requiresReplace));
    }

    public override ValueTask<TerraformModelResult<FileManagedResourceModel>> ApplyAsync(
        FileManagedResourceModel? priorState,
        FileManagedResourceModel? request,
        TerraformResourceContext<FileProviderState> context,
        CancellationToken cancellationToken)
    {
        var providerState = context.ProviderState;

        if (request is null)
        {
            DeletePriorFileIfPresent(providerState, priorState);
            return ValueTask.FromResult(new TerraformModelResult<FileManagedResourceModel>(null));
        }

        var path = request.Path.RequireValue();
        var content = request.Content.RequireValue();
        var absolutePath = FileProviderModel.ResolvePath(providerState, path);
        var directory = Path.GetDirectoryName(absolutePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(absolutePath, content);

        var materialized = FileProviderModel.ReadExisting(providerState, path);
        return ValueTask.FromResult(new TerraformModelResult<FileManagedResourceModel>(FileProviderModel.ToResourceModel(materialized)));
    }

    public override ValueTask<TerraformImportResult<FileManagedResourceModel>> ImportAsync(
        string id,
        TerraformResourceContext<FileProviderState> context,
        CancellationToken cancellationToken)
    {
        var providerState = context.ProviderState;
        var absolutePath = FileProviderModel.ResolvePath(providerState, id);

        if (!File.Exists(absolutePath))
        {
            return ValueTask.FromResult(
                new TerraformImportResult<FileManagedResourceModel>(
                    [],
                    [
                        TerraformPluginDotnet.Diagnostics.TerraformDiagnostic.Error(
                            "Import target not found",
                            $"No file exists at '{absolutePath}'."),
                    ]));
        }

        var materialized = FileProviderModel.ReadExisting(providerState, id);

        return ValueTask.FromResult(
            new TerraformImportResult<FileManagedResourceModel>(
                [
                    FileProviderModel.ToResourceModel(materialized),
                ]));
    }

    private static IReadOnlyList<TerraformAttributePath>? GetReplacePathsIfNeeded(
        FileProviderState providerState,
        FileManagedResourceModel? priorState,
        string nextAbsolutePath)
    {
        if (priorState is null)
        {
            return null;
        }

        var priorPathValue = priorState.Path;

        if (priorPathValue.IsNull || priorPathValue.IsUnknown)
        {
            return null;
        }

        var priorAbsolutePath = FileProviderModel.ResolvePath(providerState, priorPathValue.RequireValue());

        return string.Equals(priorAbsolutePath, nextAbsolutePath, StringComparison.Ordinal)
            ? null
            : [TerraformAttributePath.Root("path")];
    }

    private static void DeletePriorFileIfPresent(FileProviderState providerState, FileManagedResourceModel? priorState)
    {
        if (priorState is null)
        {
            return;
        }

        var priorPathValue = priorState.Path;

        if (priorPathValue.IsNull || priorPathValue.IsUnknown)
        {
            return;
        }

        var absolutePath = FileProviderModel.ResolvePath(providerState, priorPathValue.RequireValue());

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }
    }
}
