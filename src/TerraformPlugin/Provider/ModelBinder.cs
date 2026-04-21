using System.Collections;
using System.Globalization;
using System.Reflection;
using TerraformPlugin.Schema;
using TerraformPlugin.Types;

namespace TerraformPlugin.Provider;

internal static class ModelBinder
{
    public static T Bind<T>(DynamicValue value) =>
        (T)Bind(typeof(T), value)!;

    public static DynamicValue Unbind<T>(T model) => Unbind(typeof(T), model);

    internal static object? Bind(Type targetType, DynamicValue value) => BindCore(targetType, value);

    internal static DynamicValue Unbind(Type targetType, object? model) => UnbindCore(targetType, model);

    private static object? BindCore(Type targetType, DynamicValue value)
    {

        if (IsNullableValueOfT(targetType, out Type wrappedType))
            return value.IsNull ? null : BindWrappedValue(wrappedType, value);

        if (ModelConventions.TryGetValueType(targetType, out wrappedType))
            return BindWrappedValue(wrappedType, value);

        if (value.IsUnknown)
            throw new InvalidOperationException($"Cannot bind unknown Terraform value to CLR type '{targetType.Name}'. Use TF<T> for unknown-aware properties.");

        if (value.IsNull)
            return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null
                ? throw new InvalidOperationException($"Cannot bind null Terraform value to non-nullable CLR type '{targetType.Name}'.")
                : null;

        var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (nonNullableType == typeof(string))
            return value.AsString();

        if (nonNullableType == typeof(bool))
            return value.AsBoolean();

        if (IsNumberType(nonNullableType))
            return ConvertNumber(nonNullableType, value.AsNumber());

        if (TryGetDictionaryValueType(nonNullableType, out var dictionaryValueType))
            return BindDictionary(nonNullableType, dictionaryValueType, value);

        if (TryGetSetElementType(nonNullableType, out var setElementType))
            return BindSet(nonNullableType, setElementType, value);

        if (TryGetEnumerableElementType(nonNullableType, out var enumerableElementType))
            return BindEnumerable(nonNullableType, enumerableElementType, value);

        return BindObject(nonNullableType, value);
    }

    private static DynamicValue UnbindCore(Type targetType, object? model)
    {
        if (IsNullableValueOfT(targetType, out var wrappedType))
            return model is null
                ? DynamicValue.Null(InferTFType(wrappedType))
                : UnbindWrappedValue(wrappedType, model);

        if (ModelConventions.TryGetValueType(targetType, out wrappedType))
            return UnbindWrappedValue(wrappedType, model!);

        if (model is null)
            return DynamicValue.Null(InferTFType(targetType));

        targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (targetType == typeof(string))
            return DynamicValue.String((string)model);

        if (targetType == typeof(bool))
            return DynamicValue.Bool((bool)model);

        if (IsNumberType(targetType))
            return DynamicValue.Number(ToTFNumber(targetType, model));

        if (TryGetDictionaryValueType(targetType, out var dictionaryValueType))
            return UnbindDictionary(targetType, dictionaryValueType, model);

        if (TryGetSetElementType(targetType, out var setElementType))
            return UnbindSet(targetType, setElementType, model);

        if (TryGetEnumerableElementType(targetType, out var enumerableElementType))
            return UnbindEnumerable(targetType, enumerableElementType, model);

        return UnbindObject(targetType, model);
    }

    private static object BindWrappedValue(Type wrappedType, DynamicValue value)
    {
        var genericType = typeof(TF<>).MakeGenericType(wrappedType);
        var methodName = value.State switch
        {
            ValueState.Known => nameof(TF<>.Known),
            ValueState.Null => nameof(TF<>.Null),
            ValueState.Unknown => nameof(TF<>.Unknown),
            _ => throw new InvalidOperationException($"Unsupported Terraform value state '{value.State}'."),
        };

        if (value.IsKnown)
        {
            var innerValue = BindCore(wrappedType, value);
            return genericType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [innerValue])!;
        }

        return genericType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [])!;
    }

    private static DynamicValue UnbindWrappedValue(Type wrappedType, object wrappedValue)
    {
        var state = (ValueState)wrappedValue.GetType().GetProperty(nameof(TF<>.State))!.GetValue(wrappedValue)!;

        return state switch
        {
            ValueState.Null => DynamicValue.Null(InferTFType(wrappedType)),
            ValueState.Unknown => DynamicValue.Unknown(InferTFType(wrappedType)),
            ValueState.Known => Unbind(
                wrappedType,
                wrappedValue.GetType().GetMethod(nameof(TF<>.RequireValue))!.Invoke(wrappedValue, [])!),
            _ => throw new InvalidOperationException($"Unsupported Terraform value state '{state}'."),
        };
    }

    private static object BindObject(Type targetType, DynamicValue value)
    {
        var instance = Activator.CreateInstance(targetType)
            ?? throw new InvalidOperationException($"Could not create an instance of '{targetType.Name}'.");

        foreach (var member in ModelConventions.GetIncludedMembers(targetType))
        {
            var memberName = ModelConventions.GetSchemaMemberName(member);
            var memberValue = value.GetAttribute(memberName);
            var boundValue = BindCore(ModelConventions.GetMemberType(member), memberValue);
            SetMemberValue(instance, member, boundValue);
        }

        return instance;
    }

    private static DynamicValue UnbindObject(Type targetType, object model)
    {
        var attributes = new Dictionary<string, DynamicValue>(StringComparer.Ordinal);

        foreach (var member in ModelConventions.GetIncludedMembers(targetType))
        {
            var memberName = ModelConventions.GetSchemaMemberName(member);
            var memberValue = GetMemberValue(model, member);
            attributes[memberName] = UnbindCore(ModelConventions.GetMemberType(member), memberValue);
        }

        return DynamicValue.Object(DeclarativeSchema.For(targetType).Block.ValueType(), attributes);
    }

    private static object BindEnumerable(Type targetType, Type elementType, DynamicValue value)
    {
        var items = value.AsSequence().Select(item => BindCore(elementType, item)).ToArray();

        if (targetType.IsArray)
        {
            var array = Array.CreateInstance(elementType, items.Length);

            for (var index = 0; index < items.Length; index++)
                array.SetValue(items[index], index);

            return array;
        }

        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;

        foreach (var item in items)
            list.Add(item);

        return list;
    }

    private static object BindSet(Type targetType, Type elementType, DynamicValue value)
    {
        var set = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(elementType))!;
        var addMethod = set.GetType().GetMethod("Add")!;

        foreach (var item in value.AsSequence())
            addMethod.Invoke(set, [BindCore(elementType, item)]);

        return set;
    }

    private static object BindDictionary(Type targetType, Type valueType, DynamicValue value)
    {
        var dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType))!;

        foreach (var pair in value.AsObject())
            dictionary[pair.Key] = BindCore(valueType, pair.Value);

        return dictionary;
    }

    private static DynamicValue UnbindEnumerable(Type targetType, Type elementType, object model)
    {
        var values = ((IEnumerable)model).Cast<object?>().Select(item => UnbindCore(elementType, item)).ToArray();
        return DynamicValue.List(InferTFType(elementType), values);
    }

    private static DynamicValue UnbindSet(Type targetType, Type elementType, object model)
    {
        var values = ((IEnumerable)model).Cast<object?>().Select(item => UnbindCore(elementType, item)).ToArray();
        return DynamicValue.Set(InferTFType(elementType), values);
    }

    private static DynamicValue UnbindDictionary(Type targetType, Type valueType, object model)
    {
        var values = new Dictionary<string, DynamicValue>(StringComparer.Ordinal);

        foreach (DictionaryEntry entry in (IDictionary)model)
            values[(string)entry.Key] = UnbindCore(valueType, entry.Value);

        return DynamicValue.Map(InferTFType(valueType), values);
    }

    internal static TFType InferTFType(Type type)
    {
        if (ModelConventions.TryGetValueType(type, out var wrappedType))
            return InferTFType(wrappedType);

        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string))
            return TFType.String;

        if (type == typeof(bool))
            return TFType.Bool;

        if (IsNumberType(type))
            return TFType.Number;

        if (TryGetDictionaryValueType(type, out var dictionaryValueType))
            return new TFMapType(InferTFType(dictionaryValueType));

        if (TryGetSetElementType(type, out var setElementType))
            return new TFSetType(InferTFType(setElementType));

        if (TryGetEnumerableElementType(type, out var enumerableElementType))
            return new TFListType(InferTFType(enumerableElementType));

        return DeclarativeSchema.For(type).Block.ValueType();
    }

    private static object ConvertNumber(Type targetType, TFNumber number) =>
        targetType == typeof(byte) ? byte.Parse(number.Raw, CultureInfo.InvariantCulture) :
        targetType == typeof(sbyte) ? sbyte.Parse(number.Raw, CultureInfo.InvariantCulture) :
        targetType == typeof(short) ? short.Parse(number.Raw, CultureInfo.InvariantCulture) :
        targetType == typeof(ushort) ? ushort.Parse(number.Raw, CultureInfo.InvariantCulture) :
        targetType == typeof(int) ? int.Parse(number.Raw, CultureInfo.InvariantCulture) :
        targetType == typeof(uint) ? uint.Parse(number.Raw, CultureInfo.InvariantCulture) :
        targetType == typeof(long) ? long.Parse(number.Raw, CultureInfo.InvariantCulture) :
        targetType == typeof(ulong) ? ulong.Parse(number.Raw, CultureInfo.InvariantCulture) :
        targetType == typeof(float) ? float.Parse(number.Raw, CultureInfo.InvariantCulture) :
        targetType == typeof(double) ? double.Parse(number.Raw, CultureInfo.InvariantCulture) :
        targetType == typeof(decimal) ? decimal.Parse(number.Raw, CultureInfo.InvariantCulture) :
        throw new InvalidOperationException($"Unsupported CLR number type '{targetType.Name}'.");

    private static TFNumber ToTFNumber(Type targetType, object value) =>
        targetType == typeof(byte) ? TFNumber.Parse(((byte)value).ToString(CultureInfo.InvariantCulture)) :
        targetType == typeof(sbyte) ? TFNumber.Parse(((sbyte)value).ToString(CultureInfo.InvariantCulture)) :
        targetType == typeof(short) ? TFNumber.Parse(((short)value).ToString(CultureInfo.InvariantCulture)) :
        targetType == typeof(ushort) ? TFNumber.Parse(((ushort)value).ToString(CultureInfo.InvariantCulture)) :
        targetType == typeof(int) ? TFNumber.Parse(((int)value).ToString(CultureInfo.InvariantCulture)) :
        targetType == typeof(uint) ? TFNumber.Parse(((uint)value).ToString(CultureInfo.InvariantCulture)) :
        targetType == typeof(long) ? TFNumber.Parse(((long)value).ToString(CultureInfo.InvariantCulture)) :
        targetType == typeof(ulong) ? TFNumber.Parse(((ulong)value).ToString(CultureInfo.InvariantCulture)) :
        targetType == typeof(float) ? TFNumber.Parse(((float)value).ToString("R", CultureInfo.InvariantCulture)) :
        targetType == typeof(double) ? TFNumber.Parse(((double)value).ToString("R", CultureInfo.InvariantCulture)) :
        targetType == typeof(decimal) ? TFNumber.Parse(((decimal)value).ToString(CultureInfo.InvariantCulture)) :
        throw new InvalidOperationException($"Unsupported CLR number type '{targetType.Name}'.");

    private static object? GetMemberValue(object instance, MemberInfo member) =>
        member switch
        {
            PropertyInfo property => property.GetValue(instance),
            FieldInfo field => field.GetValue(instance),
            _ => throw new InvalidOperationException($"Unsupported member type '{member.MemberType}'."),
        };

    private static void SetMemberValue(object instance, MemberInfo member, object? value)
    {
        switch (member)
        {
            case PropertyInfo property:
                if (property.SetMethod is not null)
                {
                    property.SetValue(instance, value);
                    return;
                }

                if (ModelConventions.TryGetBackingField(property, out var backingField))
                {
                    backingField.SetValue(instance, value);
                    return;
                }

                throw new InvalidOperationException(
                    $"Property '{property.DeclaringType?.Name}.{property.Name}' is not writable or bindable.");
            case FieldInfo field:
                field.SetValue(instance, value);
                return;
            default:
                throw new InvalidOperationException($"Unsupported member type '{member.MemberType}'.");
        }
    }

    private static bool IsNullableValueOfT(Type type, out Type wrappedType)
    {
        var nullableType = Nullable.GetUnderlyingType(type);

        if (nullableType is not null && ModelConventions.TryGetValueType(nullableType, out wrappedType))
            return true;

        wrappedType = null!;
        return false;
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
            return type;

        foreach (var interfaceType in type.GetInterfaces())
            if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == genericTypeDefinition)
                return interfaceType;

        return null;
    }
}
