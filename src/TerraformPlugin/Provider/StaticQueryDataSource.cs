using TerraformPlugin.Diagnostics;
using TerraformPlugin.Schema;
using TerraformPlugin.Types;
using TerraformPlugin.Validation;

namespace TerraformPlugin.Provider;

internal sealed class StaticGetDataSource<TResource, TParams, TProviderState> : IDataSource
    where TResource : Resource<TResource, TProviderState>, IDataSource<TResource, TParams>
    where TParams : new()
{
    public ComponentSchema Schema { get; } =
        QuerySchemaBuilder.BuildSingle<TResource, TParams>();

    public ValueTask<ValidateResult> ValidateConfigAsync(DataSourceValidateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var parameters = ModelBinder.Bind<TParams>(request.Config);
            ArgumentNullException.ThrowIfNull(parameters);
            return ValueTask.FromResult(new ValidateResult(Validator.Validate(parameters)));
        }
        catch (Exception exception) when (!RuntimeDiagnostics.ShouldRethrow(exception))
        {
            return ValueTask.FromResult(
                new ValidateResult(
                    RuntimeDiagnostics.FromException("Data source configuration validation failed", exception)));
        }
    }

    public async ValueTask<ReadResult> ReadAsync(DataSourceReadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var providerState = RequireProviderState(request.ProviderState);
            var parameters = ModelBinder.Bind<TParams>(request.Config);
            ArgumentNullException.ThrowIfNull(parameters);
            var model = await TResource.GetAsync(
                parameters,
                new DataSourceContext<TProviderState>(providerState),
                cancellationToken).ConfigureAwait(false);

            return QueryDataSourceResults.BuildSingle<TResource, TParams>(request.Config, parameters, model, Schema);
        }
        catch (Exception exception) when (!RuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new ReadResult(
                DynamicValue.Null(Schema.Block.ValueType()),
                Diagnostics: RuntimeDiagnostics.FromException("Data source read failed", exception));
        }
    }

    private static TProviderState RequireProviderState(object? providerState) =>
        providerState is TProviderState typed
            ? typed
            : throw new InvalidOperationException("Provider state was not available for the data source operation.");
}

internal sealed class StaticListResource<TResource, TParams, TProviderState> : IListResource
    where TResource : Resource<TResource, TProviderState>, IListResource<TResource, TParams>
    where TParams : new()
{
    public ComponentSchema Schema { get; } =
        QuerySchemaBuilder.BuildListConfig<TParams>();

    public ComponentSchema ResourceSchema { get; } =
        DeclarativeSchema.For<TResource>();

    public IdentitySchema IdentitySchema { get; } =
        ResourceIdentityConvention.InferDefault(DeclarativeSchema.For<TResource>())
        ?? throw new InvalidOperationException(
            $"Resource '{typeof(TResource).Name}' must expose a supported identity schema before it can back a Terraform list resource.");

    public ValueTask<ValidateResult> ValidateConfigAsync(ListResourceValidateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var parameters = ModelBinder.Bind<TParams>(request.Config);
            ArgumentNullException.ThrowIfNull(parameters);
            return ValueTask.FromResult(
                new ValidateResult(
                    [
                        .. Validator.Validate(parameters),
                        .. QueryValidation.ValidateListOptions(request.IncludeResourceObject, request.Limit),
                    ]));
        }
        catch (Exception exception) when (!RuntimeDiagnostics.ShouldRethrow(exception))
        {
            return ValueTask.FromResult(
                new ValidateResult(
                    RuntimeDiagnostics.FromException("List resource configuration validation failed", exception)));
        }
    }

    public async IAsyncEnumerable<ListEvent> ListAsync(
        ListResourceRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IReadOnlyList<TResource> models;
        IReadOnlyList<Diagnostic>? diagnostics = null;

        try
        {
            var providerState = RequireProviderState(request.ProviderState);
            var parameters = ModelBinder.Bind<TParams>(request.Config);
            ArgumentNullException.ThrowIfNull(parameters);
            models = await TResource.ListAsync(
                parameters,
                new ListResourceContext<TProviderState>(providerState, request.IncludeResourceObject, request.Limit),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!RuntimeDiagnostics.ShouldRethrow(exception))
        {
            diagnostics = RuntimeDiagnostics.FromException("List resource execution failed", exception);
            models = [];
        }

        if (diagnostics is not null)
        {
            yield return new ListEvent(Diagnostics: diagnostics);
            yield break;
        }

        foreach (var model in models)
        {
            var resourceObject = ModelBinder.Unbind(model);
            var identity = QueryListResults.BuildIdentity(resourceObject, IdentitySchema);
            var displayName = QueryListResults.BuildDisplayName(identity);

            yield return new ListEvent(
                Identity: identity,
                DisplayName: displayName,
                ResourceObject: request.IncludeResourceObject ? resourceObject : null);
        }
    }

    private static TProviderState RequireProviderState(object? providerState) =>
        providerState is TProviderState typed
            ? typed
            : throw new InvalidOperationException("Provider state was not available for the data source operation.");
}

internal static class QuerySchemaBuilder
{
    public static ComponentSchema BuildSingle<TResource, TParams>()
    {
        var resourceSchema = DeclarativeSchema.For<TResource>().Block;
        var parameterSchema = DeclarativeSchema.For<TParams>().Block;
        var attributes = new Dictionary<string, SchemaAttribute>(StringComparer.Ordinal);
        var nestedBlocks = new Dictionary<string, SchemaNestedBlock>(StringComparer.Ordinal);

        foreach (var attribute in resourceSchema.Attributes.Values)
        {
            if (parameterSchema.Attributes.TryGetValue(attribute.Name, out var parameter))
            {
                attributes[attribute.Name] = attribute with
                {
                    Required = parameter.Required,
                    Optional = parameter.Optional,
                    Computed = false,
                };
            }
            else
            {
                attributes[attribute.Name] = attribute with
                {
                    Required = false,
                    Optional = false,
                    Computed = true,
                };
            }
        }

        foreach (var attribute in parameterSchema.Attributes.Values)
        {
            if (!attributes.ContainsKey(attribute.Name))
            {
                attributes[attribute.Name] = attribute;
            }
        }

        foreach (var block in resourceSchema.NestedBlocks.Values)
        {
            nestedBlocks[block.TypeName] = block;
        }

        foreach (var block in parameterSchema.NestedBlocks.Values)
        {
            if (!nestedBlocks.ContainsKey(block.TypeName))
            {
                nestedBlocks[block.TypeName] = block;
            }
        }

        return new ComponentSchema(new SchemaBlock(attributes, nestedBlocks));
    }

    public static ComponentSchema BuildListConfig<TParams>() =>
        DeclarativeSchema.For<TParams>();
}

internal static class QueryDataSourceResults
{
    public static ReadResult BuildSingle<TResource, TParams>(
        DynamicValue config,
        TParams parameters,
        TResource? model,
        ComponentSchema schema)
    {
        if (model is null)
        {
            return new ReadResult(DynamicValue.Null(schema.Block.ValueType()));
        }

        var values = new Dictionary<string, DynamicValue>(StringComparer.Ordinal);
        var modelValues = ModelBinder.Unbind(model).AsObject();
        var parameterValues = ModelBinder.Unbind(parameters).AsObject();

        foreach (var pair in modelValues)
        {
            values[pair.Key] = pair.Value;
        }

        foreach (var pair in parameterValues)
        {
            if (!values.ContainsKey(pair.Key))
            {
                values[pair.Key] = config.GetAttribute(pair.Key);
            }
        }

        return new ReadResult(DynamicValue.Object(schema.Block.ValueType(), values));
    }
}

internal static class QueryListResults
{
    public static DynamicValue BuildIdentity(
        DynamicValue resourceObject,
        IdentitySchema identitySchema)
    {
        var resourceValues = resourceObject.AsObject();
        var identityValues = new Dictionary<string, DynamicValue>(StringComparer.Ordinal);

        foreach (var attribute in identitySchema.Attributes)
        {
            if (!resourceValues.TryGetValue(attribute.Name, out var value))
            {
                throw new InvalidOperationException(
                    $"List result did not contain the identity attribute '{attribute.Name}'.");
            }

            identityValues[attribute.Name] = value;
        }

        return DynamicValue.Object(identitySchema.ValueType(), identityValues);
    }

    public static string? BuildDisplayName(DynamicValue identity)
    {
        var identityValues = identity.AsObject();

        return identityValues.TryGetValue("id", out var id) && id.IsKnown
            ? id.AsString()
            : null;
    }
}

internal static class QueryValidation
{
    public static IReadOnlyList<Diagnostic> ValidateListOptions(
        DynamicValue includeResourceObject,
        DynamicValue limit)
    {
        var diagnostics = new List<Diagnostic>();

        if (includeResourceObject.IsKnown && includeResourceObject.Type != TFType.Bool)
        {
            diagnostics.Add(Diagnostic.Error(
                "Invalid include_resource",
                "include_resource must be a boolean value.",
                AttributePath.Root("include_resource")));
        }

        if (limit.IsKnown)
        {
            var number = limit.AsNumber();

            if (!number.TryGetInt64(out var value) || value < 0)
            {
                diagnostics.Add(Diagnostic.Error(
                    "Invalid limit",
                    "limit must be a whole number greater than or equal to zero.",
                    AttributePath.Root("limit")));
            }
        }

        return diagnostics;
    }
}
