using TerraformPlugin.Diagnostics;
using TerraformPlugin.Schema;

namespace TerraformPlugin.Provider;

internal interface IResource
{
    ComponentSchema Schema { get; }
    IdentitySchema? IdentitySchema { get; }

    ValueTask<ValidateResult> ValidateConfigAsync(ValidateRequest request, CancellationToken cancellationToken);
    ValueTask<ReadResult> ReadAsync(ResourceReadRequest request, CancellationToken cancellationToken);
    ValueTask<PlanResult> PlanAsync(ResourcePlanRequest request, CancellationToken cancellationToken);
    ValueTask<ApplyResult> ApplyAsync(ResourceApplyRequest request, CancellationToken cancellationToken);
    ValueTask<ImportResult> ImportAsync(ResourceImportRequest request, CancellationToken cancellationToken);
}

internal abstract class ResourceBase : IResource
{
    public abstract ComponentSchema Schema { get; }
    public virtual IdentitySchema? IdentitySchema => null;

    public virtual ValueTask<ValidateResult> ValidateConfigAsync(ValidateRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ValidateResult.Empty);

    public abstract ValueTask<ReadResult> ReadAsync(ResourceReadRequest request, CancellationToken cancellationToken);
    public abstract ValueTask<PlanResult> PlanAsync(ResourcePlanRequest request, CancellationToken cancellationToken);
    public abstract ValueTask<ApplyResult> ApplyAsync(ResourceApplyRequest request, CancellationToken cancellationToken);

    public virtual ValueTask<ImportResult> ImportAsync(ResourceImportRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ImportResult(
            [],
            [
                Diagnostic.Error(
                    "Import Not Supported",
                    "This resource does not implement import support.")
            ]));
}
