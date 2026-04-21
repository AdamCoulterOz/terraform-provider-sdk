using System.Reflection;
using System.Runtime.CompilerServices;
using TerraformPluginDotnet.Types;

namespace TerraformPluginDotnet.Schema;

internal static class TerraformModelConventions
{
    public static IEnumerable<MemberInfo> GetIncludedMembers(Type modelType) =>
        modelType
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(static member => member.MemberType is MemberTypes.Property or MemberTypes.Field)
            .Where(ShouldIncludeMember)
            .OrderBy(GetSchemaMemberName, StringComparer.Ordinal);

    public static string GetSchemaMemberName(MemberInfo member)
    {
        var attributeName = member.GetCustomAttribute<TerraformAttributeAttribute>(inherit: true)?.Name;

        if (!string.IsNullOrWhiteSpace(attributeName))
        {
            return attributeName;
        }

        var nestedBlockName = member.GetCustomAttribute<TerraformNestedBlockAttribute>(inherit: true)?.TypeName;

        if (!string.IsNullOrWhiteSpace(nestedBlockName))
        {
            return nestedBlockName;
        }

        return ToSnakeCase(member.Name);
    }

    public static Type GetMemberType(MemberInfo member) =>
        member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => throw new InvalidOperationException($"Unsupported schema member '{member.MemberType}'."),
        };

    public static bool IsNestedBlockMember(MemberInfo member)
    {
        var attribute = member.GetCustomAttribute<TerraformAttributeAttribute>(inherit: true);
        var nestedBlock = member.GetCustomAttribute<TerraformNestedBlockAttribute>(inherit: true);

        if (attribute is not null && nestedBlock is not null)
        {
            throw new InvalidOperationException(
                $"Member '{member.DeclaringType?.Name}.{member.Name}' cannot declare both TerraformAttribute and TerraformNestedBlock.");
        }

        if (attribute is not null)
        {
            return false;
        }

        if (nestedBlock is not null)
        {
            return true;
        }

        return IsImplicitNestedBlockType(GetMemberType(member));
    }

    public static bool TryGetTerraformValueType(Type type, out Type wrappedType)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(TF<>))
        {
            wrappedType = type.GetGenericArguments()[0];
            return true;
        }

        wrappedType = null!;
        return false;
    }

    public static bool IsInputMember(MemberInfo member)
    {
        if (member.GetCustomAttribute<TerraformAttributeAttribute>(inherit: true) is { Computed: true })
        {
            return false;
        }

        return member switch
        {
            PropertyInfo property => property.SetMethod is not null || TryGetBackingField(property, out _),
            FieldInfo field => !field.IsInitOnly,
            _ => false,
        };
    }

    public static bool IsRequiredMember(MemberInfo member) =>
        member.GetCustomAttribute<RequiredMemberAttribute>(inherit: true) is not null;

    public static bool TryGetBackingField(PropertyInfo property, out FieldInfo field)
    {
        field = property.DeclaringType?.GetField(
            $"<{property.Name}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return field is not null;
    }

    private static bool ShouldIncludeMember(MemberInfo member) =>
        member.GetCustomAttribute<TerraformAttributeAttribute>(inherit: true) is not null ||
        member.GetCustomAttribute<TerraformNestedBlockAttribute>(inherit: true) is not null ||
        member switch
        {
            PropertyInfo property => property.GetMethod?.IsPublic == true &&
                                     property.GetIndexParameters().Length == 0 &&
                                     (property.SetMethod?.IsPublic == true || TryGetBackingField(property, out _)),
            FieldInfo field => !field.IsInitOnly,
            _ => false,
        };

    private static bool IsImplicitNestedBlockType(Type type)
    {
        if (TryGetTerraformValueType(type, out _))
        {
            return false;
        }

        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string) || type == typeof(bool) || IsNumberType(type))
        {
            return false;
        }

        if (TryGetDictionaryValueType(type, out var dictionaryValueType))
        {
            return IsImplicitNestedBlockType(dictionaryValueType);
        }

        if (TryGetSetElementType(type, out var setElementType))
        {
            return IsImplicitNestedBlockType(setElementType);
        }

        if (TryGetEnumerableElementType(type, out var enumerableElementType))
        {
            return IsImplicitNestedBlockType(enumerableElementType);
        }

        return true;
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
