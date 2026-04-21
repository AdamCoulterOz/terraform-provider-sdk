using TerraformPlugin.Schema;
using TerraformPlugin.Types;

namespace TerraformPlugin.Tests;

public sealed class DeclarativeSchemaTests
{
    [Fact]
    public void ForModel_BuildsAttributesAndNestedBlocks()
    {
        var schema = DeclarativeSchema.For<ExampleResourceModel>();

        Assert.Equal(3, schema.Version);
        Assert.Equal("Example resource schema.", schema.Block.Description);

        var name = schema.Block.Attributes["name"];
        Assert.True(name.Required);
        Assert.Equal(TFType.String, name.Type);

        var description = schema.Block.Attributes["description"];
        Assert.True(description.Optional);
        Assert.Equal(TFType.String, description.Type);

        var tags = schema.Block.Attributes["tags"];
        var tagsType = Assert.IsType<TFMapType>(tags.Type);
        Assert.Equal(TFType.String, tagsType.ElementType);

        var settings = schema.Block.Attributes["settings"];
        var settingsType = Assert.IsType<TerraformObjectType>(settings.Type);
        Assert.Contains("enabled", settingsType.AttributeTypes.Keys);
        Assert.Contains("notes", settingsType.OptionalAttributes);

        var child = schema.Block.NestedBlocks["child"];
        Assert.Equal(SchemaNestingMode.Single, child.Nesting);
        Assert.Contains("value", child.Block.Attributes.Keys);
        Assert.Contains("computed_value", child.Block.Attributes.Keys);
    }

    [Fact]
    public void ForModel_UsesConventionBasedNamesAndNestedBlocks()
    {
        var schema = DeclarativeSchema.For<ConventionResourceModel>();

        var displayName = schema.Block.Attributes["display_name"];
        Assert.True(displayName.Required);
        Assert.Equal(TFType.String, displayName.Type);

        var timeouts = schema.Block.NestedBlocks["timeouts"];
        Assert.Equal(SchemaNestingMode.Single, timeouts.Nesting);
        Assert.True(timeouts.Block.Attributes["create"].Optional);
        Assert.True(timeouts.Block.Attributes["delete"].Optional);
    }

    [Fact]
    public void ForModel_SupportsAnnotatedFields()
    {
        var schema = DeclarativeSchema.For<FieldBackedModel>();
        var attribute = schema.Block.Attributes["field_name"];

        Assert.True(attribute.Optional);
        Assert.Equal(TFType.String, attribute.Type);
    }

    [Fact]
    public void ForModel_InfersListNestedBlockNesting()
    {
        var schema = DeclarativeSchema.For<ListBlockModel>();
        var block = schema.Block.NestedBlocks["items"];

        Assert.Equal(SchemaNestingMode.List, block.Nesting);
        Assert.Contains("name", block.Block.Attributes.Keys);
    }

    [Fact]
    public void ForModel_InfersWrappedTerraformValueTypes()
    {
        var schema = DeclarativeSchema.For<WrappedValueModel>();
        var name = schema.Block.Attributes["name"];
        var id = schema.Block.Attributes["id"];

        Assert.True(name.Required);
        Assert.Equal(TFType.String, name.Type);
        Assert.True(id.Computed);
        Assert.Equal(TFType.String, id.Type);
    }

    [Fact]
    public void ForModel_RejectsRecursiveObjectAttributeModels()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => DeclarativeSchema.For<RecursiveObjectModel>());

        Assert.Contains("Recursive Terraform schema model", exception.Message, StringComparison.Ordinal);
    }

    [SchemaModel(Version = 3, Description = "Example resource schema.")]
    private sealed class ExampleResourceModel
    {
        [TFAttribute]
        public string Name { get; init; } = string.Empty;

        [TFAttribute]
        public string? Description { get; init; }

        [TFAttribute]
        public Dictionary<string, string> Tags { get; init; } = new(StringComparer.Ordinal);

        [TFAttribute]
        public ExampleSettingsModel Settings { get; init; } = new();

        [NestedBlock]
        public ExampleChildBlockModel Child { get; init; } = new();
    }

    private sealed class ExampleSettingsModel
    {
        [TFAttribute]
        public bool Enabled { get; init; }

        [TFAttribute]
        public string? Notes { get; init; }
    }

    private sealed class ExampleChildBlockModel
    {
        [TFAttribute]
        public long Value { get; init; }

        [TFAttribute(Computed = true)]
        public string ComputedValue { get; init; } = string.Empty;
    }

    private sealed class FieldBackedModel
    {
        [TFAttribute("field_name")]
        public string? Name { get; init; }
    }

    private sealed class ListBlockModel
    {
        [NestedBlock]
        public List<ListBlockItemModel> Items { get; init; } = [];
    }

    private sealed class ListBlockItemModel
    {
        [TFAttribute]
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
        [TFAttribute]
        public TF<string> Name { get; init; }

        [TFAttribute(Computed = true)]
        public TF<string> Id { get; init; }
    }

    private sealed class RecursiveObjectModel
    {
        [TFAttribute]
        public RecursiveObjectModel Child { get; init; } = null!;
    }
}
