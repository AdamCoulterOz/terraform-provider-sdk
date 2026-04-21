using System.Reflection;
using TerraformPlugin.Types;

namespace TerraformPlugin.Schema;

public static class DeclarativeSchema
{
    public static ComponentSchema For<T>() => For(typeof(T));

    public static ComponentSchema For(Type modelType)
    {
        var context = new SchemaInferenceContext(new NullabilityInfoContext());
        var schemaModel = modelType.GetCustomAttribute<SchemaModelAttribute>();

        return new ComponentSchema(
            BuildBlock(modelType, schemaModel, context),
            schemaModel?.Version ?? 0);
    }

    private static SchemaBlock BuildBlock(Type modelType, SchemaModelAttribute? schemaModel, SchemaInferenceContext context)
    {
        var attributes = new Dictionary<string, SchemaAttribute>(StringComparer.Ordinal);
        var nestedBlocks = new Dictionary<string, SchemaNestedBlock>(StringComparer.Ordinal);

        foreach (var member in ModelConventions.GetIncludedMembers(modelType))
        {
            if (ModelConventions.IsNestedBlockMember(member))
            {
                var nestedBlock = member.GetCustomAttribute<NestedBlockAttribute>(inherit: true);
                var schema = BuildNestedBlock(member, nestedBlock, context);
                nestedBlocks.Add(schema.TypeName, schema);
            }
            else
            {
                var attribute = member.GetCustomAttribute<TFAttributeAttribute>(inherit: true);
                var schema = BuildAttribute(member, attribute, context);
                attributes.Add(schema.Name, schema);
            }
        }

        return new SchemaBlock(
            attributes,
            nestedBlocks,
            schemaModel?.Description ?? string.Empty,
            schemaModel?.DescriptionKind ?? SchemaStringKind.Plain,
            schemaModel?.Deprecated ?? false,
            schemaModel?.DeprecationMessage ?? string.Empty);
    }

    private static SchemaAttribute BuildAttribute(MemberInfo member, TFAttributeAttribute? attribute, SchemaInferenceContext context)
    {
        var memberType = ModelConventions.GetMemberType(member);
        var terraformType = InferTerraformType(member, memberType, context);
        var semantics = InferAttributeSemantics(member, memberType, attribute, context);
        var name = ModelConventions.GetSchemaMemberName(member);

        return new SchemaAttribute(
            name,
            terraformType,
            semantics.Required,
            semantics.Optional,
            semantics.Computed,
            attribute?.Sensitive ?? false,
            attribute?.WriteOnly ?? false,
            attribute?.Description ?? string.Empty,
            attribute?.DescriptionKind ?? SchemaStringKind.Plain,
            attribute?.Deprecated ?? false,
            attribute?.DeprecationMessage ?? string.Empty);
    }

    private static SchemaNestedBlock BuildNestedBlock(MemberInfo member, NestedBlockAttribute? attribute, SchemaInferenceContext context)
    {
        var memberType = ModelConventions.GetMemberType(member);
        var nesting = attribute?.Nesting ?? InferNestedBlockNesting(memberType);
        var blockType = UnwrapNestedBlockModelType(memberType, nesting);
        var typeName = ModelConventions.GetSchemaMemberName(member);

        return new SchemaNestedBlock(
            typeName,
            nesting,
            BuildBlock(blockType, blockType.GetCustomAttribute<SchemaModelAttribute>(), context),
            attribute?.MinItems ?? 0,
            attribute?.MaxItems ?? 0);
    }

    private static TFType InferTerraformType(MemberInfo member, Type memberType, SchemaInferenceContext context)
    {
        if (ModelConventions.TryGetValueType(memberType, out var wrappedType))
        {
            return InferTerraformType(member, wrappedType, context);
        }

        memberType = Nullable.GetUnderlyingType(memberType) ?? memberType;

        if (memberType == typeof(string))
        {
            return TFType.String;
        }

        if (memberType == typeof(bool))
        {
            return TFType.Bool;
        }

        if (IsNumberType(memberType))
        {
            return TFType.Number;
        }

        if (TryGetDictionaryValueType(memberType, out var dictionaryValueType))
        {
            return new TFMapType(InferTerraformType(member, dictionaryValueType, context));
        }

        if (TryGetSetElementType(memberType, out var setElementType))
        {
            return new TFSetType(InferTerraformType(member, setElementType, context));
        }

        if (TryGetEnumerableElementType(memberType, out var enumerableElementType))
        {
            return new TFListType(InferTerraformType(member, enumerableElementType, context));
        }

        return InferObjectType(memberType, context);
    }

    private static TerraformObjectType InferObjectType(Type modelType, SchemaInferenceContext context)
    {
        if (!context.ActiveObjectTypes.Add(modelType))
        {
            throw new InvalidOperationException(
                $"Recursive Terraform schema model '{modelType.Name}' is not supported. Break the cycle or model the relationship as an identifier instead.");
        }

        var attributeTypes = new Dictionary<string, TFType>(StringComparer.Ordinal);
        var optionalAttributes = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            foreach (var member in ModelConventions.GetIncludedMembers(modelType))
            {
                if (ModelConventions.IsNestedBlockMember(member))
                {
                    throw new InvalidOperationException(
                        $"Nested blocks are not valid inside Terraform object attribute models. Member '{modelType.Name}.{member.Name}' must be represented as an attribute object instead.");
                }

                var attribute = member.GetCustomAttribute<TFAttributeAttribute>(inherit: true);
                var memberType = ModelConventions.GetMemberType(member);
                var terraformType = InferTerraformType(member, memberType, context);
                var semantics = InferAttributeSemantics(member, memberType, attribute, context);
                var name = ModelConventions.GetSchemaMemberName(member);

                attributeTypes.Add(name, terraformType);

                if (semantics.Optional)
                {
                    optionalAttributes.Add(name);
                }
            }

            return new TerraformObjectType(attributeTypes, optionalAttributes);
        }
        finally
        {
            context.ActiveObjectTypes.Remove(modelType);
        }
    }

    private static (bool Required, bool Optional, bool Computed) InferAttributeSemantics(
        MemberInfo member,
        Type memberType,
        TFAttributeAttribute? attribute,
        SchemaInferenceContext context)
    {
        if (attribute?.Required == true && attribute.Optional)
        {
            throw new InvalidOperationException(
                $"Member '{member.DeclaringType?.Name}.{member.Name}' cannot be both required and optional.");
        }

        if (attribute?.Required == true && attribute.Computed)
        {
            throw new InvalidOperationException(
                $"Member '{member.DeclaringType?.Name}.{member.Name}' cannot be both required and computed.");
        }

        if (attribute is not null && (attribute.Required || attribute.Optional || attribute.Computed))
        {
            return (attribute.Required, attribute.Optional, attribute.Computed);
        }

        if (!ModelConventions.IsInputMember(member))
        {
            return (false, false, true);
        }

        if (ModelConventions.IsRequiredMember(member))
        {
            return (true, false, false);
        }

        return IsNullable(member, memberType, context)
            ? (false, true, false)
            : (true, false, false);
    }

    private static bool IsNullable(MemberInfo member, Type memberType, SchemaInferenceContext context)
    {
        if (Nullable.GetUnderlyingType(memberType) is not null)
        {
            return true;
        }

        if (memberType.IsValueType)
        {
            return false;
        }

        return member switch
        {
            PropertyInfo property => context.Nullability.Create(property).ReadState == NullabilityState.Nullable,
            FieldInfo field => context.Nullability.Create(field).ReadState == NullabilityState.Nullable,
            _ => false,
        };
    }

    private static SchemaNestingMode InferNestedBlockNesting(Type memberType)
    {
        if (TryGetDictionaryValueType(memberType, out _))
        {
            return SchemaNestingMode.Map;
        }

        if (TryGetSetElementType(memberType, out _))
        {
            return SchemaNestingMode.Set;
        }

        if (TryGetEnumerableElementType(memberType, out _))
        {
            return SchemaNestingMode.List;
        }

        return SchemaNestingMode.Single;
    }

    private static Type UnwrapNestedBlockModelType(Type memberType, SchemaNestingMode nesting) =>
        nesting switch
        {
            SchemaNestingMode.Map => TryGetDictionaryValueType(memberType, out var valueType)
                ? valueType
                : throw new InvalidOperationException($"Nested block member '{memberType.Name}' must be a dictionary for map nesting."),
            SchemaNestingMode.List => TryGetEnumerableElementType(memberType, out var listType)
                ? listType
                : throw new InvalidOperationException($"Nested block member '{memberType.Name}' must be an enumerable for list nesting."),
            SchemaNestingMode.Set => TryGetSetElementType(memberType, out var setType)
                ? setType
                : throw new InvalidOperationException($"Nested block member '{memberType.Name}' must be a set for set nesting."),
            SchemaNestingMode.Single or SchemaNestingMode.Group => Nullable.GetUnderlyingType(memberType) ?? memberType,
            _ => throw new InvalidOperationException($"Unsupported nested block nesting mode '{nesting}'."),
        };

    private static bool IsNumberType(Type type) =>
        type == typeof(byte) ||
        type == typeof(sbyte) ||
        type == typeof(short) ||
        type == typeof(ushort) ||
        type == typeof(int) ||
        type == typeof(uint) ||
        type == typeof(long) ||
        type == typeof(ulong) ||
        type == typeof(float) ||
        type == typeof(double) ||
        type == typeof(decimal);

    private static bool TryGetDictionaryValueType(Type type, out Type valueType)
    {
        var dictionaryType = GetGenericType(type, typeof(IDictionary<,>))
            ?? GetGenericType(type, typeof(IReadOnlyDictionary<,>));

        if (dictionaryType is not null && dictionaryType.GetGenericArguments()[0] == typeof(string))
        {
            valueType = dictionaryType.GetGenericArguments()[1];
            return true;
        }

        valueType = null!;
        return false;
    }

    private static bool TryGetSetElementType(Type type, out Type elementType)
    {
        var setType = GetGenericType(type, typeof(ISet<>))
            ?? GetGenericType(type, typeof(IReadOnlySet<>));

        if (setType is not null)
        {
            elementType = setType.GetGenericArguments()[0];
            return true;
        }

        elementType = null!;
        return false;
    }

    private static bool TryGetEnumerableElementType(Type type, out Type elementType)
    {
        if (type == typeof(string))
        {
            elementType = null!;
            return false;
        }

        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        var enumerableType = GetGenericType(type, typeof(IEnumerable<>));

        if (enumerableType is not null)
        {
            elementType = enumerableType.GetGenericArguments()[0];
            return true;
        }

        elementType = null!;
        return false;
    }

    private static Type? GetGenericType(Type type, Type genericTypeDefinition)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == genericTypeDefinition)
        {
            return type;
        }

        foreach (var interfaceType in type.GetInterfaces())
        {
            if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == genericTypeDefinition)
            {
                return interfaceType;
            }
        }

        return null;
    }

    private sealed record SchemaInferenceContext(NullabilityInfoContext Nullability)
    {
        public HashSet<Type> ActiveObjectTypes { get; } = new();
    }
}
