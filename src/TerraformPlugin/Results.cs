using TerraformPlugin.Diagnostics;
using TerraformPlugin.Types;

namespace TerraformPlugin;

public sealed record ProviderContext(
    string TerraformVersion,
    bool DeferralAllowed);

public sealed record ResourceContext<TProviderState>(
    TProviderState ProviderState,
    byte[]? PrivateState = null,
    byte[]? PriorPrivateState = null,
    byte[]? PlannedPrivateState = null);

public record DataSourceContext(object? ProviderState)
{
    public T RequireProviderState<T>() =>
        ProviderState is T typed
            ? typed
            : throw new InvalidOperationException("Provider state was not available for the data source operation.");
}

public sealed record DataSourceContext<TProviderState> : DataSourceContext
{
    public DataSourceContext(TProviderState providerState)
        : base(providerState)
    {
        ProviderState = providerState;
    }

    public new TProviderState ProviderState { get; }
}

public record ListResourceContext(object? ProviderState, bool IncludeResourceObject, long Limit)
{
    public T RequireProviderState<T>() =>
        ProviderState is T typed
            ? typed
            : throw new InvalidOperationException("Provider state was not available for the list resource operation.");
}

public sealed record ListResourceContext<TProviderState> : ListResourceContext
{
    public ListResourceContext(TProviderState providerState, bool includeResourceObject, long limit)
        : base(providerState, includeResourceObject, limit)
    {
        ProviderState = providerState;
    }

    public new TProviderState ProviderState { get; }
}

public sealed record ModelResult<TModel>(
    TModel? Model,
    byte[]? PrivateState = null,
    IReadOnlyList<Diagnostic>? Diagnostics = null);

public sealed record PlanResult<TModel>(
    TModel? PlannedState,
    byte[]? PlannedPrivateState = null,
    IReadOnlyList<AttributePath>? RequiresReplace = null,
    IReadOnlyList<Diagnostic>? Diagnostics = null);

public sealed record ImportResult<TModel>(
    IReadOnlyList<TModel> Resources,
    IReadOnlyList<Diagnostic>? Diagnostics = null);
