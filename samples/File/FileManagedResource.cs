using TerraformPluginDotnet;
using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;
using System.Diagnostics.CodeAnalysis;

namespace File;

[method: SetsRequiredMembers]
internal sealed class FileManagedResource() : TerraformResource<FileManagedResource, FileProviderState>
{
    public override string Name => "managed";

    [TerraformAttribute(Description = "Path to the managed file.")]
    public required TF<string> Path { get; init; }

    [TerraformAttribute(Description = "Desired file content.")]
    public required TF<string> Content { get; init; }

    [TerraformAttribute(Computed = true, Description = "Canonical absolute path.")]
    public TF<string> AbsolutePath { get; init; }

    [TerraformAttribute(Computed = true, Description = "SHA-256 of the file content.")]
    public TF<string> Sha256 { get; init; }

    [TerraformAttribute(Computed = true, Description = "Provider-assigned resource identifier.")]
    public TF<string> Id { get; init; }

    public override ValueTask<IReadOnlyList<TerraformDiagnostic>> ValidateConfigAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(FileProviderModel.ValidatePathValue(Path, "path"));

    public override ValueTask<TerraformModelResult<FileManagedResource>> ReadAsync(
        TerraformResourceContext<FileProviderState> context,
        CancellationToken cancellationToken)
    {
        if (Path.IsNull || Path.IsUnknown)
            return ValueTask.FromResult(new TerraformModelResult<FileManagedResource>(null, PrivateState: context.PrivateState));

        var absolutePath = FileProviderModel.ResolvePath(context.ProviderState, Path.RequireValue());

        if (!System.IO.File.Exists(absolutePath))
            return ValueTask.FromResult(new TerraformModelResult<FileManagedResource>(null, PrivateState: context.PrivateState));

        return ValueTask.FromResult(
            new TerraformModelResult<FileManagedResource>(
                FileProviderModel.ToResource(FileProviderModel.ReadExisting(context.ProviderState, Path.RequireValue())),
                PrivateState: context.PrivateState));
    }

    public override ValueTask<TerraformPlanResult<FileManagedResource>> PlanAsync(
        FileManagedResource? priorState,
        TerraformResourceContext<FileProviderState> context,
        CancellationToken cancellationToken)
    {
        if (Path.IsUnknown || Content.IsUnknown)
            return ValueTask.FromResult(
                new TerraformPlanResult<FileManagedResource>(
                    FileProviderModel.UnknownResourcePlannedState(Path, Content),
                    PlannedPrivateState: context.PriorPrivateState));

        var materialized = FileProviderModel.Materialize(context.ProviderState, Path.RequireValue(), Content.RequireValue());
        var requiresReplace = GetReplacePathsIfNeeded(context.ProviderState, priorState, materialized.AbsolutePath);

        return ValueTask.FromResult(
            new TerraformPlanResult<FileManagedResource>(
                FileProviderModel.ToResource(materialized),
                PlannedPrivateState: context.PriorPrivateState,
                RequiresReplace: requiresReplace));
    }

    public override ValueTask<TerraformModelResult<FileManagedResource>> ApplyAsync(
        FileManagedResource? priorState,
        TerraformResourceContext<FileProviderState> context,
        CancellationToken cancellationToken)
    {
        var path = Path.RequireValue();
        var content = Content.RequireValue();
        var absolutePath = FileProviderModel.ResolvePath(context.ProviderState, path);
        var directory = System.IO.Path.GetDirectoryName(absolutePath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        System.IO.File.WriteAllText(absolutePath, content);

        var materialized = FileProviderModel.ReadExisting(context.ProviderState, path);
        return ValueTask.FromResult(
            new TerraformModelResult<FileManagedResource>(
                FileProviderModel.ToResource(materialized),
                PrivateState: context.PlannedPrivateState));
    }

    public override ValueTask<TerraformModelResult<FileManagedResource>> DeleteAsync(
        FileManagedResource? priorState,
        TerraformResourceContext<FileProviderState> context,
        CancellationToken cancellationToken)
    {
        DeletePriorFileIfPresent(context.ProviderState, priorState);
        return ValueTask.FromResult(new TerraformModelResult<FileManagedResource>(null, PrivateState: context.PlannedPrivateState));
    }

    public override ValueTask<TerraformImportResult<FileManagedResource>> ImportAsync(
        string id,
        TerraformResourceContext<FileProviderState> context,
        CancellationToken cancellationToken)
    {
        var absolutePath = FileProviderModel.ResolvePath(context.ProviderState, id);

        if (!System.IO.File.Exists(absolutePath))
            return ValueTask.FromResult(
                new TerraformImportResult<FileManagedResource>(
                    [],
                    [
                        TerraformDiagnostic.Error(
                            "Import target not found",
                            $"No file exists at '{absolutePath}'."),
                    ]));

        var materialized = FileProviderModel.ReadExisting(context.ProviderState, id);

        return ValueTask.FromResult(
            new TerraformImportResult<FileManagedResource>(
                [
                    FileProviderModel.ToResource(materialized),
                ]));
    }

    [DataSourceQuery(Name = "read")]
    public static ValueTask<FileManagedResource?> GetAsync(
        FileProviderState providerState,
        TF<string> path,
        CancellationToken cancellationToken)
    {
        var absolutePath = FileProviderModel.ResolvePath(providerState, path.RequireValue());

        if (!System.IO.File.Exists(absolutePath))
            return ValueTask.FromResult<FileManagedResource?>(null);

        return ValueTask.FromResult<FileManagedResource?>(FileProviderModel.ToResource(FileProviderModel.ReadExisting(providerState, path.RequireValue())));
    }

    private static IReadOnlyList<TerraformAttributePath>? GetReplacePathsIfNeeded(
        FileProviderState providerState,
        FileManagedResource? priorState,
        string nextAbsolutePath)
    {
        if (priorState is null || priorState.Path.IsNull || priorState.Path.IsUnknown)
            return null;

        var priorAbsolutePath = FileProviderModel.ResolvePath(providerState, priorState.Path.RequireValue());

        return string.Equals(priorAbsolutePath, nextAbsolutePath, StringComparison.Ordinal)
            ? null
            : [TerraformAttributePath.Root("path")];
    }

    private static void DeletePriorFileIfPresent(FileProviderState providerState, FileManagedResource? priorState)
    {
        if (priorState is null || priorState.Path.IsNull || priorState.Path.IsUnknown)
            return;

        var absolutePath = FileProviderModel.ResolvePath(providerState, priorState.Path.RequireValue());

        if (System.IO.File.Exists(absolutePath))
            System.IO.File.Delete(absolutePath);
    }
}
