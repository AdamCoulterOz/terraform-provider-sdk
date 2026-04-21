using System.Reflection;
using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Provider;
using TerraformPluginDotnet.Schema;

namespace TerraformPluginDotnet;

public interface ITerraformResource<TSelf, TProviderState>
    where TSelf : ITerraformResource<TSelf, TProviderState>, new()
{
    ValueTask<IReadOnlyList<TerraformDiagnostic>> ValidateConfigAsync(CancellationToken cancellationToken);

    ValueTask<TerraformModelResult<TSelf>> ReadAsync(
        TerraformResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    ValueTask<TerraformPlanResult<TSelf>> PlanAsync(
        TSelf? priorState,
        TerraformResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    ValueTask<TerraformModelResult<TSelf>> ApplyAsync(
        TSelf? priorState,
        TerraformResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    ValueTask<TerraformModelResult<TSelf>> DeleteAsync(
        TSelf? priorState,
        TerraformResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    ValueTask<TerraformImportResult<TSelf>> ImportAsync(
        string id,
        TerraformResourceContext<TProviderState> context,
        CancellationToken cancellationToken);
}

public abstract class TerraformResource<TProviderState>
{
    public abstract string Name { get; }

    internal abstract ITerraformResource ToInternalResource();

    internal virtual IEnumerable<(string Name, ITerraformDataSource DataSource)> ToGeneratedDataSources() =>
        [];
}

public abstract class TerraformResource<TSelf, TProviderState> : TerraformResource<TProviderState>, ITerraformResource<TSelf, TProviderState>
    where TSelf : TerraformResource<TSelf, TProviderState>, new()
{
    public TerraformComponentSchema Schema { get; } = TerraformDeclarativeSchema.For<TSelf>();

    public virtual ValueTask<IReadOnlyList<TerraformDiagnostic>> ValidateConfigAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<TerraformDiagnostic>>([]);

    public abstract ValueTask<TerraformModelResult<TSelf>> ReadAsync(
        TerraformResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    public abstract ValueTask<TerraformPlanResult<TSelf>> PlanAsync(
        TSelf? priorState,
        TerraformResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    public abstract ValueTask<TerraformModelResult<TSelf>> ApplyAsync(
        TSelf? priorState,
        TerraformResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    public virtual ValueTask<TerraformModelResult<TSelf>> DeleteAsync(
        TSelf? priorState,
        TerraformResourceContext<TProviderState> context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new TerraformModelResult<TSelf>(null));

    public virtual ValueTask<TerraformImportResult<TSelf>> ImportAsync(
        string id,
        TerraformResourceContext<TProviderState> context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            new TerraformImportResult<TSelf>(
                [],
                [
                    TerraformDiagnostic.Error(
                        "Import Not Supported",
                        "This resource does not implement import support."),
                ]));

    internal override ITerraformResource ToInternalResource() => new TypedResourceAdapter<TSelf, TProviderState>(this);

    internal override IEnumerable<(string Name, ITerraformDataSource DataSource)> ToGeneratedDataSources()
    {
        foreach (var method in typeof(TSelf).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            var attribute = method.GetCustomAttribute<DataSourceQueryAttribute>(inherit: true);

            if (attribute is null)
            {
                continue;
            }

            yield return (
                string.IsNullOrWhiteSpace(attribute.Name) ? Name : attribute.Name!,
                new ReflectedQueryDataSource<TSelf, TProviderState>(method, attribute));
        }
    }
}
