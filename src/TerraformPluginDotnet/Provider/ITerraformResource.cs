using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Provider;

public interface ITerraformResource
{
    TerraformComponentSchema Schema { get; }

    ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformResourceValidateRequest request, CancellationToken cancellationToken);
    ValueTask<TerraformReadResult> ReadAsync(TerraformResourceReadRequest request, CancellationToken cancellationToken);
    ValueTask<TerraformPlanResult> PlanAsync(TerraformResourcePlanRequest request, CancellationToken cancellationToken);
    ValueTask<TerraformApplyResult> ApplyAsync(TerraformResourceApplyRequest request, CancellationToken cancellationToken);
    ValueTask<TerraformImportResult> ImportAsync(TerraformResourceImportRequest request, CancellationToken cancellationToken);
}

public abstract class TerraformResourceBase : ITerraformResource
{
    public abstract TerraformComponentSchema Schema { get; }

    public virtual ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformResourceValidateRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(TerraformValidateResult.Empty);

    public abstract ValueTask<TerraformReadResult> ReadAsync(TerraformResourceReadRequest request, CancellationToken cancellationToken);
    public abstract ValueTask<TerraformPlanResult> PlanAsync(TerraformResourcePlanRequest request, CancellationToken cancellationToken);
    public abstract ValueTask<TerraformApplyResult> ApplyAsync(TerraformResourceApplyRequest request, CancellationToken cancellationToken);

    public virtual ValueTask<TerraformImportResult> ImportAsync(TerraformResourceImportRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new TerraformImportResult(
            [],
            [
                TerraformDiagnostic.Error(
                    "Import Not Supported",
                    "This resource does not implement import support.")
            ]));
}
