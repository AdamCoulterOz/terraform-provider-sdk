using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Provider;

internal sealed class ReflectedQueryDataSource<TResource, TProviderState> : ITerraformDataSource
    where TResource : TerraformResource<TResource, TProviderState>, new()
{
    private readonly MethodInfo _method;
    private readonly QueryReturnShape _returnShape;
    private readonly QueryParameterDescriptor[] _inputParameters;
    private readonly IReadOnlyDictionary<string, QueryParameterDescriptor> _parametersByName;
    private readonly string _itemsName;

    public ReflectedQueryDataSource(MethodInfo method, DataSourceQueryAttribute attribute)
    {
        _method = method;
        _returnShape = QueryReturnShape.For<TResource>(method);
        _inputParameters = method.GetParameters()
            .Where(static parameter => !IsInfrastructureParameter(parameter))
            .Select(static parameter => new QueryParameterDescriptor(parameter))
            .ToArray();
        _parametersByName = _inputParameters.ToDictionary(static parameter => parameter.Name, StringComparer.Ordinal);
        _itemsName = attribute.ItemsName;
        Schema = BuildSchema();
    }

    public TerraformComponentSchema Schema { get; }

    public ValueTask<TerraformValidateResult> ValidateConfigAsync(TerraformDataSourceValidateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _ = BindMethodArguments(request.Config, default, cancellationToken);
            return ValueTask.FromResult(TerraformValidateResult.Empty);
        }
        catch (Exception exception) when (!TerraformRuntimeDiagnostics.ShouldRethrow(exception))
        {
            return ValueTask.FromResult(
                new TerraformValidateResult(
                    TerraformRuntimeDiagnostics.FromException("Data source configuration validation failed", exception)));
        }
    }

    public async ValueTask<TerraformReadResult> ReadAsync(TerraformDataSourceReadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var providerState = RequireProviderState(request.ProviderState);
            var args = BindMethodArguments(request.Config, providerState, cancellationToken);
            var invocationResult = _method.Invoke(null, args);
            var queryResult = await UnwrapReturnValueAsync(invocationResult, cancellationToken).ConfigureAwait(false);

            return _returnShape.Kind switch
            {
                QueryReturnKind.Single => BuildSingleResult(request.Config, (TResource?)queryResult),
                QueryReturnKind.Many => BuildManyResult(request.Config, (IEnumerable<TResource>)queryResult!),
                _ => throw new InvalidOperationException($"Unsupported query return kind '{_returnShape.Kind}'."),
            };
        }
        catch (Exception exception) when (!TerraformRuntimeDiagnostics.ShouldRethrow(exception))
        {
            return new TerraformReadResult(
                TerraformDynamicValue.Null(Schema.Block.ValueType()),
                Diagnostics: TerraformRuntimeDiagnostics.FromException("Data source read failed", exception));
        }
    }

    private TerraformComponentSchema BuildSchema()
    {
        var resourceSchema = TerraformDeclarativeSchema.For<TResource>().Block;
        var attributes = new Dictionary<string, TerraformSchemaAttribute>(StringComparer.Ordinal);
        var nestedBlocks = new Dictionary<string, TerraformSchemaNestedBlock>(StringComparer.Ordinal);

        foreach (var attribute in resourceSchema.Attributes.Values)
        {
            if (_parametersByName.TryGetValue(attribute.Name, out var parameter))
            {
                attributes[attribute.Name] = attribute with
                {
                    Required = parameter.Required,
                    Optional = !parameter.Required,
                    Computed = false,
                };
            }
            else if (_returnShape.Kind == QueryReturnKind.Single)
            {
                attributes[attribute.Name] = attribute with
                {
                    Required = false,
                    Optional = false,
                    Computed = true,
                };
            }
        }

        foreach (var block in resourceSchema.NestedBlocks.Values)
        {
            if (_returnShape.Kind == QueryReturnKind.Single)
            {
                nestedBlocks[block.TypeName] = block;
            }
        }

        foreach (var parameter in _inputParameters)
        {
            if (attributes.ContainsKey(parameter.Name) || nestedBlocks.ContainsKey(parameter.Name))
            {
                continue;
            }

            attributes[parameter.Name] = new TerraformSchemaAttribute(
                parameter.Name,
                parameter.Type,
                Required: parameter.Required,
                Optional: !parameter.Required);
        }

        if (_returnShape.Kind == QueryReturnKind.Many)
        {
            attributes[_itemsName] = new TerraformSchemaAttribute(
                _itemsName,
                new TerraformListType(TerraformDeclarativeSchema.For<TResource>().Block.ValueType()),
                Computed: true);
        }

        return new TerraformComponentSchema(new TerraformSchemaBlock(attributes, nestedBlocks));
    }

    private object?[] BindMethodArguments(TerraformDynamicValue config, TProviderState? providerState, CancellationToken cancellationToken)
    {
        var args = new object?[_method.GetParameters().Length];
        var parameters = _method.GetParameters();

        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];

            if (parameter.ParameterType == typeof(CancellationToken))
            {
                args[index] = cancellationToken;
                continue;
            }

            if (parameter.ParameterType == typeof(TProviderState))
            {
                args[index] = providerState;
                continue;
            }

            if (parameter.ParameterType == typeof(TerraformDataSourceContext<TProviderState>))
            {
                args[index] = new TerraformDataSourceContext<TProviderState>(providerState!);
                continue;
            }

            var name = ToSnakeCase(parameter.Name ?? throw new InvalidOperationException("Query parameters must be named."));
            args[index] = TerraformModelBinder.Bind(parameter.ParameterType, config.GetAttribute(name));
        }

        return args;
    }

    private TerraformReadResult BuildSingleResult(TerraformDynamicValue config, TResource? model)
    {
        if (model is null)
        {
            return new TerraformReadResult(TerraformDynamicValue.Null(Schema.Block.ValueType()));
        }

        var resultValues = new Dictionary<string, TerraformDynamicValue>(StringComparer.Ordinal);
        var unbound = TerraformModelBinder.Unbind(model).AsObject();

        foreach (var pair in unbound)
        {
            resultValues[pair.Key] = pair.Value;
        }

        foreach (var parameter in _inputParameters)
        {
            if (!resultValues.ContainsKey(parameter.Name))
            {
                resultValues[parameter.Name] = config.GetAttribute(parameter.Name);
            }
        }

        return new TerraformReadResult(TerraformDynamicValue.Object(Schema.Block.ValueType(), resultValues));
    }

    private TerraformReadResult BuildManyResult(TerraformDynamicValue config, IEnumerable<TResource> models)
    {
        var stateValues = new Dictionary<string, TerraformDynamicValue>(StringComparer.Ordinal);

        foreach (var parameter in _inputParameters)
        {
            stateValues[parameter.Name] = config.GetAttribute(parameter.Name);
        }

        stateValues[_itemsName] = TerraformDynamicValue.List(
            TerraformDeclarativeSchema.For<TResource>().Block.ValueType(),
            models.Select(static model => TerraformModelBinder.Unbind(model)));

        return new TerraformReadResult(TerraformDynamicValue.Object(Schema.Block.ValueType(), stateValues));
    }

    private static async ValueTask<object?> UnwrapReturnValueAsync(object? value, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            return null;
        }

        if (value is Task task)
        {
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty("Result")?.GetValue(task);
        }

        var valueType = value.GetType();

        if (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var asTask = valueType.GetMethod(nameof(ValueTask<int>.AsTask))!.Invoke(value, []) as Task
                ?? throw new InvalidOperationException("Could not unwrap ValueTask result.");

            await asTask.ConfigureAwait(false);
            return asTask.GetType().GetProperty("Result")?.GetValue(asTask);
        }

        if (value is ValueTask valueTask)
        {
            await valueTask.ConfigureAwait(false);
            return null;
        }

        return value;
    }

    private static bool IsInfrastructureParameter(ParameterInfo parameter) =>
        parameter.ParameterType == typeof(CancellationToken) ||
        parameter.ParameterType == typeof(TProviderState) ||
        parameter.ParameterType == typeof(TerraformDataSourceContext<TProviderState>);

    private static TProviderState RequireProviderState(object? providerState) =>
        providerState is TProviderState typed
            ? typed
            : throw new InvalidOperationException("Provider state was not available for the data source operation.");

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

    private sealed record QueryParameterDescriptor(ParameterInfo Parameter)
    {
        public string Name { get; } = ToSnakeCase(Parameter.Name ?? throw new InvalidOperationException("Query parameters must be named."));

        public TerraformType Type { get; } = TerraformModelBinder.InferTerraformType(Parameter.ParameterType);

        public bool Required { get; } = Parameter.GetCustomAttribute<RequiredMemberAttribute>(inherit: true) is not null ||
                                        (!Parameter.HasDefaultValue && !IsNullable(Parameter.ParameterType, Parameter));

        private static bool IsNullable(Type type, ParameterInfo parameter)
        {
            if (Nullable.GetUnderlyingType(type) is not null)
            {
                return true;
            }

            if (type.IsValueType)
            {
                return false;
            }

            return new NullabilityInfoContext().Create(parameter).ReadState == NullabilityState.Nullable;
        }
    }

    private sealed record QueryReturnShape(QueryReturnKind Kind)
    {
        public static QueryReturnShape For<TExpected>(MethodInfo method)
        {
            var returnType = method.ReturnType;

            if (returnType == typeof(Task) || returnType == typeof(ValueTask))
            {
                throw new InvalidOperationException(
                    $"Data source query '{method.DeclaringType?.Name}.{method.Name}' must return a value.");
            }

            returnType = UnwrapAsyncType(returnType);

            if (returnType == typeof(TExpected))
            {
                return new QueryReturnShape(QueryReturnKind.Single);
            }

            var enumerableType = GetEnumerableElementType(returnType);

            if (enumerableType == typeof(TExpected))
            {
                return new QueryReturnShape(QueryReturnKind.Many);
            }

            throw new InvalidOperationException(
                $"Data source query '{method.DeclaringType?.Name}.{method.Name}' must return '{typeof(TExpected).Name}', '{typeof(TExpected).Name}?' or a collection of '{typeof(TExpected).Name}'.");
        }

        private static Type UnwrapAsyncType(Type type)
        {
            if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
            {
                return type.GetGenericArguments()[0];
            }

            return type;
        }

        private static Type? GetEnumerableElementType(Type type)
        {
            if (type == typeof(string))
            {
                return null;
            }

            if (type.IsArray)
            {
                return type.GetElementType();
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return type.GetGenericArguments()[0];
            }

            foreach (var interfaceType in type.GetInterfaces())
            {
                if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return interfaceType.GetGenericArguments()[0];
                }
            }

            return null;
        }
    }

    private enum QueryReturnKind
    {
        Single,
        Many,
    }
}
