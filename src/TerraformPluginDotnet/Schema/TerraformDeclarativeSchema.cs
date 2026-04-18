using System.Collections;
using System.Reflection;
using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Schema;

public static class TerraformDeclarativeSchema
{
    private static readonly NullabilityInfoContext Nullability = new();

    public static TerraformComponentSchema For<T>() => For(typeof(T));

    public static TerraformComponentSchema For(Type modelType)
    {
        var schemaModel = modelType.GetCustomAttribute<TerraformSchemaModelAttribute>();

        return new TerraformComponentSchema(
            BuildBlock(modelType, schemaModel),
            schemaModel?.Version ?? 0);
    }

    private static TerraformSchemaBlock BuildBlock(Type modelType, TerraformSchemaModelAttribute? schemaModel)
    {
        var attributes = new Dictionary<string, TerraformSchemaAttribute>(StringComparer.Ordinal);
        var nestedBlocks = new Dictionary<string, TerraformSchemaNestedBlock>(StringComparer.Ordinal);

        foreach (var member in TerraformModelConventions.GetIncludedMembers(modelType))
        {
            if (TerraformModelConventions.IsNestedBlockMember(member))
            {
                var nestedBlock = member.GetCustomAttribute<TerraformNestedBlockAttribute>(inherit: true);
                var schema = BuildNestedBlock(member, nestedBlock);
                nestedBlocks.Add(schema.TypeName, schema);
            }
            else
            {
                var attribute = member.GetCustomAttribute<TerraformAttributeAttribute>(inherit: true);
                var schema = BuildAttribute(member, attribute);
                attributes.Add(schema.Name, schema);
            }
        }

        return new TerraformSchemaBlock(
            attributes,
            nestedBlocks,
            schemaModel?.Description ?? string.Empty,
            schemaModel?.DescriptionKind ?? TerraformSchemaStringKind.Plain,
            schemaModel?.Deprecated ?? false,
            schemaModel?.DeprecationMessage ?? string.Empty);
    }

    private static TerraformSchemaAttribute BuildAttribute(MemberInfo member, TerraformAttributeAttribute? attribute)
    {
        var memberType = TerraformModelConventions.GetMemberType(member);
        var terraformType = InferTerraformType(member, memberType);
        var semantics = InferAttributeSemantics(member, memberType, attribute);
        var name = TerraformModelConventions.GetSchemaMemberName(member);

        return new TerraformSchemaAttribute(
            name,
            terraformType,
            semantics.Required,
            semantics.Optional,
            semantics.Computed,
            attribute?.Sensitive ?? false,
            attribute?.WriteOnly ?? false,
            attribute?.Description ?? string.Empty,
            attribute?.DescriptionKind ?? TerraformSchemaStringKind.Plain,
            attribute?.Deprecated ?? false,
            attribute?.DeprecationMessage ?? string.Empty);
    }

    private static TerraformSchemaNestedBlock BuildNestedBlock(MemberInfo member, TerraformNestedBlockAttribute? attribute)
    {
        var memberType = TerraformModelConventions.GetMemberType(member);
        var nesting = attribute?.Nesting ?? InferNestedBlockNesting(memberType);
        var blockType = UnwrapNestedBlockModelType(memberType, nesting);
        var typeName = TerraformModelConventions.GetSchemaMemberName(member);

        return new TerraformSchemaNestedBlock(
            typeName,
            nesting,
            BuildBlock(blockType, blockType.GetCustomAttribute<TerraformSchemaModelAttribute>()),
            attribute?.MinItems ?? 0,
            attribute?.MaxItems ?? 0);
    }

    private static TerraformType InferTerraformType(MemberInfo member, Type memberType)
    {
        if (TerraformModelConventions.TryGetTerraformValueType(memberType, out var wrappedType))
        {
            return InferTerraformType(member, wrappedType);
        }

        memberType = Nullable.GetUnderlyingType(memberType) ?? memberType;

        if (memberType == typeof(string))
        {
            return TerraformType.String;
        }

        if (memberType == typeof(bool))
        {
            return TerraformType.Bool;
        }

        if (IsNumberType(memberType))
        {
            return TerraformType.Number;
        }

        if (TryGetDictionaryValueType(memberType, out var dictionaryValueType))
        {
            return new TerraformMapType(InferTerraformType(member, dictionaryValueType));
        }

        if (TryGetSetElementType(memberType, out var setElementType))
        {
            return new TerraformSetType(InferTerraformType(member, setElementType));
        }

        if (TryGetEnumerableElementType(memberType, out var enumerableElementType))
        {
            return new TerraformListType(InferTerraformType(member, enumerableElementType));
        }

        return InferObjectType(memberType);
    }

    private static TerraformObjectType InferObjectType(Type modelType)
    {
        var attributeTypes = new Dictionary<string, TerraformType>(StringComparer.Ordinal);
        var optionalAttributes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in TerraformModelConventions.GetIncludedMembers(modelType))
        {
            if (TerraformModelConventions.IsNestedBlockMember(member))
            {
                throw new InvalidOperationException(
                    $"Nested blocks are not valid inside Terraform object attribute models. Member '{modelType.Name}.{member.Name}' must be represented as an attribute object instead.");
            }

            var attribute = member.GetCustomAttribute<TerraformAttributeAttribute>(inherit: true);
            var memberType = TerraformModelConventions.GetMemberType(member);
            var terraformType = InferTerraformType(member, memberType);
            var semantics = InferAttributeSemantics(member, memberType, attribute);
            var name = TerraformModelConventions.GetSchemaMemberName(member);

            attributeTypes.Add(name, terraformType);

            if (semantics.Optional)
            {
                optionalAttributes.Add(name);
            }
        }

        return new TerraformObjectType(attributeTypes, optionalAttributes);
    }

    private static (bool Required, bool Optional, bool Computed) InferAttributeSemantics(
        MemberInfo member,
        Type memberType,
        TerraformAttributeAttribute? attribute)
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

        return IsNullable(member, memberType)
            ? (false, true, false)
            : (true, false, false);
    }

    private static bool IsNullable(MemberInfo member, Type memberType)
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
            PropertyInfo property => Nullability.Create(property).ReadState == NullabilityState.Nullable,
            FieldInfo field => Nullability.Create(field).ReadState == NullabilityState.Nullable,
            _ => false,
        };
    }

    private static TerraformSchemaNestingMode InferNestedBlockNesting(Type memberType)
    {
        if (TryGetDictionaryValueType(memberType, out _))
        {
            return TerraformSchemaNestingMode.Map;
        }

        if (TryGetSetElementType(memberType, out _))
        {
            return TerraformSchemaNestingMode.Set;
        }

        if (TryGetEnumerableElementType(memberType, out _))
        {
            return TerraformSchemaNestingMode.List;
        }

        return TerraformSchemaNestingMode.Single;
    }

    private static Type UnwrapNestedBlockModelType(Type memberType, TerraformSchemaNestingMode nesting) =>
        nesting switch
        {
            TerraformSchemaNestingMode.Map => TryGetDictionaryValueType(memberType, out var valueType)
                ? valueType
                : throw new InvalidOperationException($"Nested block member '{memberType.Name}' must be a dictionary for map nesting."),
            TerraformSchemaNestingMode.List => TryGetEnumerableElementType(memberType, out var listType)
                ? listType
                : throw new InvalidOperationException($"Nested block member '{memberType.Name}' must be an enumerable for list nesting."),
            TerraformSchemaNestingMode.Set => TryGetSetElementType(memberType, out var setType)
                ? setType
                : throw new InvalidOperationException($"Nested block member '{memberType.Name}' must be a set for set nesting."),
            TerraformSchemaNestingMode.Single or TerraformSchemaNestingMode.Group => Nullable.GetUnderlyingType(memberType) ?? memberType,
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
}
