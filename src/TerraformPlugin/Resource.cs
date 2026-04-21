using System.Reflection;
using TerraformPlugin.Diagnostics;
using TerraformPlugin.Provider;
using TerraformPlugin.Schema;
using TerraformPlugin.Validation;

namespace TerraformPlugin;

public interface IResource<TSelf, TProviderState>
    where TSelf : IResource<TSelf, TProviderState>
{
    ValueTask<IReadOnlyList<Diagnostic>> ValidateConfigAsync(CancellationToken cancellationToken);

    ValueTask<ModelResult<TSelf>> ReadAsync(
        ResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    ValueTask<PlanResult<TSelf>> PlanAsync(
        TSelf? priorState,
        ResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    ValueTask<ModelResult<TSelf>> ApplyAsync(
        TSelf? priorState,
        ResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    ValueTask<ModelResult<TSelf>> DeleteAsync(
        TSelf? priorState,
        ResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    ValueTask<ImportResult<TSelf>> ImportAsync(
        string id,
        ResourceContext<TProviderState> context,
        CancellationToken cancellationToken);
}

public abstract class Resource<TProviderState>
{
    public abstract string Name { get; }

    internal abstract IResource ToInternalResource();
    internal virtual IEnumerable<(string Name, IListResource ListResource)> ToGeneratedListResources() =>
        [];

    internal virtual IEnumerable<(string Name, IDataSource DataSource)> ToGeneratedDataSources() =>
        [];
}

internal sealed class RegisteredTerraformResource<TResource, TProviderState> : Resource<TProviderState>
    where TResource : Resource<TResource, TProviderState>
{
    private readonly Lazy<TResource> _definition = new(CreateDefinition, LazyThreadSafetyMode.ExecutionAndPublication);

    public override string Name => Definition.Name;

    internal override IResource ToInternalResource() => Definition.ToInternalResource();

    internal override IEnumerable<(string Name, IListResource ListResource)> ToGeneratedListResources() =>
        Definition.ToGeneratedListResources();

    internal override IEnumerable<(string Name, IDataSource DataSource)> ToGeneratedDataSources() =>
        Definition.ToGeneratedDataSources();

    private TResource Definition => _definition.Value;

    private static TResource CreateDefinition() =>
        (TResource)(Activator.CreateInstance(typeof(TResource), nonPublic: true)
            ?? throw new InvalidOperationException($"Could not create resource definition '{typeof(TResource).FullName}'."));
}

public abstract class Resource<TSelf, TProviderState> : Resource<TProviderState>, IResource<TSelf, TProviderState>
    where TSelf : Resource<TSelf, TProviderState>
{
    public override string Name => ResolveResourceName(typeof(TSelf));

    protected virtual string DefaultDataSourceName => Name;

    public ComponentSchema Schema { get; } = DeclarativeSchema.For<TSelf>();

    internal IdentitySchema? IdentitySchema { get; } =
        ResourceIdentityConvention.InferDefault(DeclarativeSchema.For<TSelf>());

    public virtual ValueTask<IReadOnlyList<Diagnostic>> ValidateConfigAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Validator.Validate(this));

    protected IReadOnlyList<Diagnostic> Validate(ResourceContext<TProviderState> context) =>
        Validator.Validate(this, context.ProviderState);

    public abstract ValueTask<ModelResult<TSelf>> ReadAsync(
        ResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    public abstract ValueTask<PlanResult<TSelf>> PlanAsync(
        TSelf? priorState,
        ResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    public abstract ValueTask<ModelResult<TSelf>> ApplyAsync(
        TSelf? priorState,
        ResourceContext<TProviderState> context,
        CancellationToken cancellationToken);

    public virtual ValueTask<ModelResult<TSelf>> DeleteAsync(
        TSelf? priorState,
        ResourceContext<TProviderState> context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ModelResult<TSelf>(null));

    public virtual ValueTask<ImportResult<TSelf>> ImportAsync(
        string id,
        ResourceContext<TProviderState> context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            new ImportResult<TSelf>(
                [],
                [
                    Diagnostic.Error(
                        "Import Not Supported",
                        "This resource does not implement import support."),
                ]));

    internal override IResource ToInternalResource() => new TypedResourceAdapter<TSelf, TProviderState>(this);

    internal override IEnumerable<(string Name, IListResource ListResource)> ToGeneratedListResources()
    {
        foreach (var generated in CreateInterfaceGeneratedListResources())
        {
            yield return generated;
        }
    }

    internal override IEnumerable<(string Name, IDataSource DataSource)> ToGeneratedDataSources()
    {
        foreach (var generated in CreateInterfaceGeneratedGetDataSources())
        {
            yield return generated;
        }

        foreach (var method in typeof(TSelf).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            var attribute = method.GetCustomAttribute<DataSourceQueryAttribute>(inherit: true);

            if (attribute is null)
            {
                continue;
            }

            yield return (
                string.IsNullOrWhiteSpace(attribute.Name) ? DefaultDataSourceName : attribute.Name!,
                new ReflectedQueryDataSource<TSelf, TProviderState>(method, attribute));
        }
    }

    private IEnumerable<(string Name, IDataSource DataSource)> CreateInterfaceGeneratedGetDataSources()
    {
        var getInterfaces = typeof(TSelf).GetInterfaces()
            .Where(static type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDataSource<,>))
            .Where(static type => type.GetGenericArguments()[0] == typeof(TSelf))
            .ToArray();

        if (getInterfaces.Length > 1)
        {
            throw new InvalidOperationException(
                $"Resource '{typeof(TSelf).Name}' declared multiple default data-source interfaces. Only one default generated data source is supported per resource.");
        }

        if (getInterfaces.Length == 1)
        {
            yield return (
                DefaultDataSourceName,
                CreateGeneratedDataSource(typeof(StaticGetDataSource<,,>), getInterfaces[0].GetGenericArguments()[1]));
        }
    }

    private IEnumerable<(string Name, IListResource ListResource)> CreateInterfaceGeneratedListResources()
    {
        var listInterfaces = typeof(TSelf).GetInterfaces()
            .Where(static type =>
                type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(IListResource<,>) ||
                 type.GetGenericTypeDefinition() == typeof(IListResource<,>)))
            .Where(static type => type.GetGenericArguments()[0] == typeof(TSelf))
            .ToArray();

        if (listInterfaces.Length > 1)
        {
            throw new InvalidOperationException(
                $"Resource '{typeof(TSelf).Name}' declared multiple default list-resource interfaces. Only one default generated list resource is supported per resource.");
        }

        if (listInterfaces.Length == 1)
        {
            yield return (
                Name,
                CreateGeneratedListResource(typeof(StaticListResource<,,>), listInterfaces[0].GetGenericArguments()[1]));
        }
    }

    private static IDataSource CreateGeneratedDataSource(Type openGenericAdapterType, Type parametersType) =>
        (IDataSource)(Activator.CreateInstance(
            openGenericAdapterType.MakeGenericType(typeof(TSelf), parametersType, typeof(TProviderState)))
            ?? throw new InvalidOperationException(
                $"Could not create generated data source for resource '{typeof(TSelf).Name}' and parameters '{parametersType.Name}'."));

    private static IListResource CreateGeneratedListResource(Type openGenericAdapterType, Type parametersType) =>
        (IListResource)(Activator.CreateInstance(
            openGenericAdapterType.MakeGenericType(typeof(TSelf), parametersType, typeof(TProviderState)))
            ?? throw new InvalidOperationException(
                $"Could not create generated list resource for resource '{typeof(TSelf).Name}' and parameters '{parametersType.Name}'."));

    protected static string ResolveResourceName(Type resourceType)
    {
        var attribute = resourceType.GetCustomAttribute<ResourceAttribute>(inherit: true);

        if (!string.IsNullOrWhiteSpace(attribute?.Name))
        {
            return attribute.Name;
        }

        return ToSnakeCase(resourceType.Name);
    }

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new System.Text.StringBuilder(name.Length + 8);

        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];

            if (char.IsUpper(current))
            {
                if (index > 0)
                {
                    var previous = name[index - 1];
                    var nextIsLower = index + 1 < name.Length && char.IsLower(name[index + 1]);

                    if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                    {
                        builder.Append('_');
                    }
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
