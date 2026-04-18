using TerraformPluginDotnet.Provider;
using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

namespace TerraformProviderFile;

internal sealed class FileManagedResource : TerraformResourceBase
{
    public override TerraformComponentSchema Schema => FileProviderModel.ResourceSchema;

    public override ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformResourceValidateRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(FileProviderModel.ValidatePathValue(request.Config, "path"));

    public override ValueTask<TerraformReadResult> ReadAsync(TerraformResourceReadRequest request, CancellationToken cancellationToken)
    {
        if (request.CurrentState.IsNull)
        {
            return ValueTask.FromResult(new TerraformReadResult(FileProviderModel.NullResourceState(), request.PrivateState));
        }

        var providerState = FileProviderModel.RequireProviderState(request.ProviderState);
        var pathValue = request.CurrentState.GetAttribute("path");

        if (pathValue.IsUnknown || pathValue.IsNull)
        {
            return ValueTask.FromResult(new TerraformReadResult(FileProviderModel.NullResourceState(), request.PrivateState));
        }

        var path = pathValue.AsString();
        var absolutePath = FileProviderModel.ResolvePath(providerState, path);

        if (!File.Exists(absolutePath))
        {
            return ValueTask.FromResult(new TerraformReadResult(FileProviderModel.NullResourceState(), request.PrivateState));
        }

        return ValueTask.FromResult(
            new TerraformReadResult(
                FileProviderModel.ToResourceValue(FileProviderModel.ReadExisting(providerState, path)),
                request.PrivateState));
    }

    public override ValueTask<TerraformPlanResult> PlanAsync(TerraformResourcePlanRequest request, CancellationToken cancellationToken)
    {
        if (request.ProposedNewState.IsNull)
        {
            return ValueTask.FromResult(new TerraformPlanResult(FileProviderModel.NullResourceState(), request.PriorPrivateState));
        }

        var pathValue = request.Config.GetAttribute("path");
        var contentValue = request.Config.GetAttribute("content");

        if (pathValue.IsUnknown || contentValue.IsUnknown)
        {
            return ValueTask.FromResult(
                new TerraformPlanResult(
                    FileProviderModel.UnknownResourcePlannedState(pathValue, contentValue),
                    request.PriorPrivateState));
        }

        var providerState = FileProviderModel.RequireProviderState(request.ProviderState);
        var materialized = FileProviderModel.Materialize(providerState, pathValue.AsString(), contentValue.AsString());
        var requiresReplace = GetReplacePathsIfNeeded(providerState, request.PriorState, materialized.AbsolutePath);

        return ValueTask.FromResult(
            new TerraformPlanResult(
                FileProviderModel.ToResourceValue(materialized),
                request.PriorPrivateState,
                requiresReplace));
    }

    public override ValueTask<TerraformApplyResult> ApplyAsync(TerraformResourceApplyRequest request, CancellationToken cancellationToken)
    {
        var providerState = FileProviderModel.RequireProviderState(request.ProviderState);

        if (request.PlannedState.IsNull)
        {
            DeletePriorFileIfPresent(providerState, request.PriorState);
            return ValueTask.FromResult(new TerraformApplyResult(FileProviderModel.NullResourceState()));
        }

        var path = request.PlannedState.GetAttribute("path").AsString();
        var content = request.PlannedState.GetAttribute("content").AsString();
        var absolutePath = FileProviderModel.ResolvePath(providerState, path);
        var directory = Path.GetDirectoryName(absolutePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(absolutePath, content);

        var materialized = FileProviderModel.ReadExisting(providerState, path);
        return ValueTask.FromResult(new TerraformApplyResult(FileProviderModel.ToResourceValue(materialized)));
    }

    public override ValueTask<TerraformImportResult> ImportAsync(TerraformResourceImportRequest request, CancellationToken cancellationToken)
    {
        var providerState = FileProviderModel.RequireProviderState(request.ProviderState);
        var absolutePath = FileProviderModel.ResolvePath(providerState, request.Id);

        if (!File.Exists(absolutePath))
        {
            return ValueTask.FromResult(
                new TerraformImportResult(
                    [],
                    [
                        TerraformPluginDotnet.Diagnostics.TerraformDiagnostic.Error(
                            "Import target not found",
                            $"No file exists at '{absolutePath}'."),
                    ]));
        }

        var materialized = FileProviderModel.ReadExisting(providerState, request.Id);

        return ValueTask.FromResult(
            new TerraformImportResult(
                [
                    new TerraformImportResource(request.TypeName, FileProviderModel.ToResourceValue(materialized)),
                ]));
    }

    private static IReadOnlyList<TerraformAttributePath>? GetReplacePathsIfNeeded(
        FileProviderState providerState,
        TerraformValue priorState,
        string nextAbsolutePath)
    {
        if (priorState.IsNull || !priorState.IsKnown)
        {
            return null;
        }

        var priorPathValue = priorState.GetAttribute("path");

        if (priorPathValue.IsNull || priorPathValue.IsUnknown)
        {
            return null;
        }

        var priorAbsolutePath = FileProviderModel.ResolvePath(providerState, priorPathValue.AsString());

        return string.Equals(priorAbsolutePath, nextAbsolutePath, StringComparison.Ordinal)
            ? null
            : [TerraformAttributePath.Root("path")];
    }

    private static void DeletePriorFileIfPresent(FileProviderState providerState, TerraformValue priorState)
    {
        if (priorState.IsNull || !priorState.IsKnown)
        {
            return;
        }

        var priorPathValue = priorState.GetAttribute("path");

        if (priorPathValue.IsNull || priorPathValue.IsUnknown)
        {
            return;
        }

        var absolutePath = FileProviderModel.ResolvePath(providerState, priorPathValue.AsString());

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }
    }
}
