namespace TerraformPluginDotnet.Types;

public enum TerraformValueState
{
    Known,
    Null,
    Unknown,
}

internal sealed class TerraformDynamicValue
{
    private TerraformDynamicValue(TerraformType type, TerraformValueState state, object? value)
    {
        Type = type;
        State = state;
        Value = value;
    }

    public TerraformType Type { get; }
    public TerraformValueState State { get; }
    public object? Value { get; }

    public bool IsKnown => State == TerraformValueState.Known;
    public bool IsNull => State == TerraformValueState.Null;
    public bool IsUnknown => State == TerraformValueState.Unknown;

    public static TerraformDynamicValue Known(TerraformType type, object value) => new(type, TerraformValueState.Known, value);
    public static TerraformDynamicValue Null(TerraformType type) => new(type, TerraformValueState.Null, null);
    public static TerraformDynamicValue Unknown(TerraformType type) => new(type, TerraformValueState.Unknown, null);

    public static TerraformDynamicValue String(string value) => Known(TerraformType.String, value);
    public static TerraformDynamicValue Number(TerraformNumber value) => Known(TerraformType.Number, value);
    public static TerraformDynamicValue Number(long value) => Number(TerraformNumber.FromInt64(value));
    public static TerraformDynamicValue Bool(bool value) => Known(TerraformType.Bool, value);

    public static TerraformDynamicValue List(TerraformType elementType, IEnumerable<TerraformDynamicValue> values) =>
        Known(new TerraformListType(elementType), values.ToArray());

    public static TerraformDynamicValue Set(TerraformType elementType, IEnumerable<TerraformDynamicValue> values) =>
        Known(new TerraformSetType(elementType), values.ToArray());

    public static TerraformDynamicValue Map(TerraformType elementType, IReadOnlyDictionary<string, TerraformDynamicValue> values) =>
        Known(new TerraformMapType(elementType), new Dictionary<string, TerraformDynamicValue>(values, StringComparer.Ordinal));

    public static TerraformDynamicValue Tuple(IReadOnlyList<TerraformType> elementTypes, IReadOnlyList<TerraformDynamicValue> values) =>
        Known(new TerraformTupleType(elementTypes), values.ToArray());

    public static TerraformDynamicValue Object(
        IReadOnlyDictionary<string, TerraformType> attributeTypes,
        IReadOnlyDictionary<string, TerraformDynamicValue> values) =>
        Known(new TerraformObjectType(attributeTypes), new Dictionary<string, TerraformDynamicValue>(values, StringComparer.Ordinal));

    public static TerraformDynamicValue Object(TerraformObjectType type, IReadOnlyDictionary<string, TerraformDynamicValue> values) =>
        Known(type, new Dictionary<string, TerraformDynamicValue>(values, StringComparer.Ordinal));

    public string AsString() => (string)RequireKnownValue(typeof(string));
    public TerraformNumber AsNumber() => (TerraformNumber)RequireKnownValue(typeof(TerraformNumber));
    public bool AsBoolean() => (bool)RequireKnownValue(typeof(bool));
    public IReadOnlyList<TerraformDynamicValue> AsSequence() => (IReadOnlyList<TerraformDynamicValue>)RequireKnownValue(typeof(IReadOnlyList<TerraformDynamicValue>));
    public IReadOnlyDictionary<string, TerraformDynamicValue> AsObject() => (IReadOnlyDictionary<string, TerraformDynamicValue>)RequireKnownValue(typeof(IReadOnlyDictionary<string, TerraformDynamicValue>));

    public TerraformDynamicValue GetAttribute(string attributeName)
    {
        var attributes = AsObject();

        if (!attributes.TryGetValue(attributeName, out var value))
            throw new KeyNotFoundException($"Terraform object does not contain attribute '{attributeName}'.");

        return value;
    }

    public string? GetOptionalString(string attributeName)
    {
        var value = GetAttribute(attributeName);

        if (value.IsNull || value.IsUnknown)
            return null;

        return value.AsString();
    }

    private object RequireKnownValue(Type expectedType)
    {
        if (!IsKnown || Value is null)
            throw new InvalidOperationException("Terraform value is not known.");

        if (!expectedType.IsAssignableFrom(Value.GetType()))
            throw new InvalidOperationException($"Terraform value is '{Value.GetType().Name}', expected '{expectedType.Name}'.");

        return Value;
    }
}
