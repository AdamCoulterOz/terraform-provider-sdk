using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Tests;

public sealed class TerraformDeclarativeSchemaTests
{
    [Fact]
    public void ForModel_BuildsAttributesAndNestedBlocks()
    {
        var schema = TerraformDeclarativeSchema.For<ExampleResourceModel>();

        Assert.Equal(3, schema.Version);
        Assert.Equal("Example resource schema.", schema.Block.Description);

        var name = schema.Block.Attributes["name"];
        Assert.True(name.Required);
        Assert.Equal(TerraformType.String, name.Type);

        var description = schema.Block.Attributes["description"];
        Assert.True(description.Optional);
        Assert.Equal(TerraformType.String, description.Type);

        var tags = schema.Block.Attributes["tags"];
        var tagsType = Assert.IsType<TerraformMapType>(tags.Type);
        Assert.Equal(TerraformType.String, tagsType.ElementType);

        var settings = schema.Block.Attributes["settings"];
        var settingsType = Assert.IsType<TerraformObjectType>(settings.Type);
        Assert.Contains("enabled", settingsType.AttributeTypes.Keys);
        Assert.Contains("notes", settingsType.OptionalAttributes);

        var child = schema.Block.NestedBlocks["child"];
        Assert.Equal(TerraformSchemaNestingMode.Single, child.Nesting);
        Assert.Contains("value", child.Block.Attributes.Keys);
        Assert.Contains("computed_value", child.Block.Attributes.Keys);
    }

    [Fact]
    public void ForModel_UsesConventionBasedNamesAndNestedBlocks()
    {
        var schema = TerraformDeclarativeSchema.For<ConventionResourceModel>();

        var displayName = schema.Block.Attributes["display_name"];
        Assert.True(displayName.Required);
        Assert.Equal(TerraformType.String, displayName.Type);

        var timeouts = schema.Block.NestedBlocks["timeouts"];
        Assert.Equal(TerraformSchemaNestingMode.Single, timeouts.Nesting);
        Assert.True(timeouts.Block.Attributes["create"].Optional);
        Assert.True(timeouts.Block.Attributes["delete"].Optional);
    }

    [Fact]
    public void ForModel_SupportsAnnotatedFields()
    {
        var schema = TerraformDeclarativeSchema.For<FieldBackedModel>();
        var attribute = schema.Block.Attributes["field_name"];

        Assert.True(attribute.Optional);
        Assert.Equal(TerraformType.String, attribute.Type);
    }

    [Fact]
    public void ForModel_InfersListNestedBlockNesting()
    {
        var schema = TerraformDeclarativeSchema.For<ListBlockModel>();
        var block = schema.Block.NestedBlocks["items"];

        Assert.Equal(TerraformSchemaNestingMode.List, block.Nesting);
        Assert.Contains("name", block.Block.Attributes.Keys);
    }

    [Fact]
    public void ForModel_InfersWrappedTerraformValueTypes()
    {
        var schema = TerraformDeclarativeSchema.For<WrappedValueModel>();
        var name = schema.Block.Attributes["name"];
        var id = schema.Block.Attributes["id"];

        Assert.True(name.Required);
        Assert.Equal(TerraformType.String, name.Type);
        Assert.True(id.Computed);
        Assert.Equal(TerraformType.String, id.Type);
    }

    [TerraformSchemaModel(Version = 3, Description = "Example resource schema.")]
    private sealed class ExampleResourceModel
    {
        [TerraformAttribute]
        public string Name { get; init; } = string.Empty;

        [TerraformAttribute]
        public string? Description { get; init; }

        [TerraformAttribute]
        public Dictionary<string, string> Tags { get; init; } = new(StringComparer.Ordinal);

        [TerraformAttribute]
        public ExampleSettingsModel Settings { get; init; } = new();

        [TerraformNestedBlock]
        public ExampleChildBlockModel Child { get; init; } = new();
    }

    private sealed class ExampleSettingsModel
    {
        [TerraformAttribute]
        public bool Enabled { get; init; }

        [TerraformAttribute]
        public string? Notes { get; init; }
    }

    private sealed class ExampleChildBlockModel
    {
        [TerraformAttribute]
        public long Value { get; init; }

        [TerraformAttribute(Computed = true)]
        public string ComputedValue { get; init; } = string.Empty;
    }

    private sealed class FieldBackedModel
    {
        [TerraformAttribute("field_name")]
        public string? Name { get; init; }
    }

    private sealed class ListBlockModel
    {
        [TerraformNestedBlock]
        public List<ListBlockItemModel> Items { get; init; } = [];
    }

    private sealed class ListBlockItemModel
    {
        [TerraformAttribute]
        public string Name { get; init; } = string.Empty;
    }

    private sealed class ConventionResourceModel
    {
        public TF<string> DisplayName { get; init; }

        public TimeoutsBlock Timeouts { get; init; } = new();
    }

    private sealed class TimeoutsBlock
    {
        public TF<string>? Create { get; init; }

        public TF<string>? Delete { get; init; }
    }

    private sealed class WrappedValueModel
    {
        [TerraformAttribute]
        public TF<string> Name { get; init; }

        [TerraformAttribute(Computed = true)]
        public TF<string> Id { get; init; }
    }
}
