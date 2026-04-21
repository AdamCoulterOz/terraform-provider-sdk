using TerraformPlugin;
using TerraformPlugin.Diagnostics;
using TerraformPlugin.Schema;
using TerraformPlugin.Types;
using TerraformPlugin.Validation;

namespace File;

[Resource("managed")]
internal sealed class FileManagedResource : Resource<FileManagedResource, FileProviderState>
{
    [TFAttribute(Description = "Path to the managed file.")]
    [NotEmpty]
    public required TF<string> Path { get; init; }

    [TFAttribute(Description = "Desired file content.")]
    public required TF<string> Content { get; init; }

    [TFAttribute(Computed = true, Description = "Canonical absolute path.")]
    public TF<string> AbsolutePath { get; init; }

    [TFAttribute(Computed = true, Description = "SHA-256 of the file content.")]
    public TF<string> Sha256 { get; init; }

    [TFAttribute(Computed = true, Description = "Provider-assigned resource identifier.")]
    public TF<string> Id { get; init; }

    public override ValueTask<ModelResult<FileManagedResource>> ReadAsync(
        ResourceContext<FileProviderState> context,
        CancellationToken cancellationToken)
    {
        if (Path.IsNull || Path.IsUnknown)
            return ValueTask.FromResult(new ModelResult<FileManagedResource>(null, PrivateState: context.PrivateState));

        var absolutePath = FileProviderModel.ResolvePath(context.ProviderState, Path.RequireValue());

        if (!System.IO.File.Exists(absolutePath))
            return ValueTask.FromResult(new ModelResult<FileManagedResource>(null, PrivateState: context.PrivateState));

        return ValueTask.FromResult(
            new ModelResult<FileManagedResource>(
                FileProviderModel.ToResource(FileProviderModel.ReadExisting(context.ProviderState, Path.RequireValue())),
                PrivateState: context.PrivateState));
    }

    public override ValueTask<PlanResult<FileManagedResource>> PlanAsync(
        FileManagedResource? priorState,
        ResourceContext<FileProviderState> context,
        CancellationToken cancellationToken)
    {
        if (Path.IsUnknown || Content.IsUnknown)
            return ValueTask.FromResult(
                new PlanResult<FileManagedResource>(
                    FileProviderModel.UnknownResourcePlannedState(Path, Content),
                    PlannedPrivateState: context.PriorPrivateState));

        var materialized = FileProviderModel.Materialize(context.ProviderState, Path.RequireValue(), Content.RequireValue());
        var requiresReplace = GetReplacePathsIfNeeded(context.ProviderState, priorState, materialized.AbsolutePath);

        return ValueTask.FromResult(
            new PlanResult<FileManagedResource>(
                FileProviderModel.ToResource(materialized),
                PlannedPrivateState: context.PriorPrivateState,
                RequiresReplace: requiresReplace));
    }

    public override ValueTask<ModelResult<FileManagedResource>> ApplyAsync(
        FileManagedResource? priorState,
        ResourceContext<FileProviderState> context,
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
            new ModelResult<FileManagedResource>(
                FileProviderModel.ToResource(materialized),
                PrivateState: context.PlannedPrivateState));
    }

    public override ValueTask<ModelResult<FileManagedResource>> DeleteAsync(
        FileManagedResource? priorState,
        ResourceContext<FileProviderState> context,
        CancellationToken cancellationToken)
    {
        DeletePriorFileIfPresent(context.ProviderState, priorState);
        return ValueTask.FromResult(new ModelResult<FileManagedResource>(null, PrivateState: context.PlannedPrivateState));
    }

    public override ValueTask<ImportResult<FileManagedResource>> ImportAsync(
        string id,
        ResourceContext<FileProviderState> context,
        CancellationToken cancellationToken)
    {
        var absolutePath = FileProviderModel.ResolvePath(context.ProviderState, id);

        if (!System.IO.File.Exists(absolutePath))
            return ValueTask.FromResult(
                new ImportResult<FileManagedResource>(
                    [],
                    [
                        Diagnostic.Error(
                            "Import target not found",
                            $"No file exists at '{absolutePath}'."),
                    ]));

        var materialized = FileProviderModel.ReadExisting(context.ProviderState, id);

        return ValueTask.FromResult(
            new ImportResult<FileManagedResource>(
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

    private static IReadOnlyList<AttributePath>? GetReplacePathsIfNeeded(
        FileProviderState providerState,
        FileManagedResource? priorState,
        string nextAbsolutePath)
    {
        if (priorState is null || priorState.Path.IsNull || priorState.Path.IsUnknown)
            return null;

        var priorAbsolutePath = FileProviderModel.ResolvePath(providerState, priorState.Path.RequireValue());

        return string.Equals(priorAbsolutePath, nextAbsolutePath, StringComparison.Ordinal)
            ? null
            : [AttributePath.Root("path")];
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
