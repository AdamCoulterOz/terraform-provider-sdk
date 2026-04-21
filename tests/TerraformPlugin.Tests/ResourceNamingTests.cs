namespace TerraformPlugin.Tests;

public sealed class ResourceNamingTests
{
    [Fact]
    public void Name_DefaultsToSnakeCaseClassName()
    {
        var resource = new DefaultNamedResource();

        Assert.Equal("default_named_resource", resource.Name);
    }

    [Fact]
    public void Name_UsesResourceAttributeOverride()
    {
        var resource = new ExplicitlyNamedResource();

        Assert.Equal("custom_name", resource.Name);
    }

    private sealed class DefaultNamedResource : Resource<DefaultNamedResource, object>
    {
        public override ValueTask<ModelResult<DefaultNamedResource>> ReadAsync(
            ResourceContext<object> context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ModelResult<DefaultNamedResource>(null));

        public override ValueTask<PlanResult<DefaultNamedResource>> PlanAsync(
            DefaultNamedResource? priorState,
            ResourceContext<object> context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PlanResult<DefaultNamedResource>(null));

        public override ValueTask<ModelResult<DefaultNamedResource>> ApplyAsync(
            DefaultNamedResource? priorState,
            ResourceContext<object> context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ModelResult<DefaultNamedResource>(null));
    }

    [Resource("custom_name")]
    private sealed class ExplicitlyNamedResource : Resource<ExplicitlyNamedResource, object>
    {
        public override ValueTask<ModelResult<ExplicitlyNamedResource>> ReadAsync(
            ResourceContext<object> context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ModelResult<ExplicitlyNamedResource>(null));

        public override ValueTask<PlanResult<ExplicitlyNamedResource>> PlanAsync(
            ExplicitlyNamedResource? priorState,
            ResourceContext<object> context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PlanResult<ExplicitlyNamedResource>(null));

        public override ValueTask<ModelResult<ExplicitlyNamedResource>> ApplyAsync(
            ExplicitlyNamedResource? priorState,
            ResourceContext<object> context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ModelResult<ExplicitlyNamedResource>(null));
    }
}
