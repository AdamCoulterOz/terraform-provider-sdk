using TerraformPlugin.Provider;
using TerraformPlugin.Schema;
using TerraformPlugin.Types;
using TerraformPlugin.Validation;

namespace TerraformPlugin.Tests;

public sealed class GeneratedQuerySurfaceTests
{
    [Fact]
    public void GeneratedDataSource_DefaultsToResourceTypeName()
    {
        var resource = new QueryableResource { DisplayName = TF<string>.Unknown() };

        var generated = Assert.Single(resource.ToGeneratedDataSources());

        Assert.Equal(resource.Name, generated.Name);
    }

    [Fact]
    public void GeneratedListResource_DefaultsToResourceTypeName()
    {
        var resource = new QueryableResource { DisplayName = TF<string>.Unknown() };

        var generated = Assert.Single(resource.ToGeneratedListResources());

        Assert.Equal(resource.Name, generated.Name);
    }

    [Fact]
    public void ResourceIdentity_IsInferredFromIdAttribute()
    {
        var resource = new QueryableResource { DisplayName = TF<string>.Unknown() };

        Assert.NotNull(resource.IdentitySchema);
        var attribute = Assert.Single(resource.IdentitySchema!.Attributes);

        Assert.Equal("id", attribute.Name);
        Assert.Equal(TFType.String, attribute.Type);
        Assert.True(attribute.RequiredForImport);
        Assert.False(attribute.OptionalForImport);
    }

    private sealed class QueryableResource :
        Resource<QueryableResource, object>,
        IDataSource<QueryableResource, QueryableResource.Get>,
        IListResource<QueryableResource, QueryableResource.List>
    {
        [TFAttribute]
        public required TF<string> DisplayName { get; init; }

        [TFAttribute(Computed = true)]
        public TF<string> Id { get; init; }

        public override ValueTask<ModelResult<QueryableResource>> ReadAsync(
            ResourceContext<object> context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ModelResult<QueryableResource>(null));

        public override ValueTask<PlanResult<QueryableResource>> PlanAsync(
            QueryableResource? priorState,
            ResourceContext<object> context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PlanResult<QueryableResource>(null));

        public override ValueTask<ModelResult<QueryableResource>> ApplyAsync(
            QueryableResource? priorState,
            ResourceContext<object> context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ModelResult<QueryableResource>(null));

        public static ValueTask<QueryableResource?> GetAsync(
            Get parameters,
            DataSourceContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<QueryableResource?>(null);

        public static ValueTask<IReadOnlyList<QueryableResource>> ListAsync(
            List parameters,
            ListResourceContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<QueryableResource>>([]);

        internal readonly record struct Get([property: NotEmpty] TF<string> DisplayName);

        internal readonly record struct List();
    }
}
