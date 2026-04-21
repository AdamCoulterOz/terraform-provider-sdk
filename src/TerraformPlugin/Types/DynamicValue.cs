namespace TerraformPlugin.Types;

public enum ValueState
{
    Known,
    Null,
    Unknown,
}

internal sealed class DynamicValue
{
    private DynamicValue(TFType type, ValueState state, object? value)
    {
        Type = type;
        State = state;
        Value = value;
    }

    public TFType Type { get; }
    public ValueState State { get; }
    public object? Value { get; }

    public bool IsKnown => State == ValueState.Known;
    public bool IsNull => State == ValueState.Null;
    public bool IsUnknown => State == ValueState.Unknown;

    public static DynamicValue Known(TFType type, object value) => new(type, ValueState.Known, value);
    public static DynamicValue Null(TFType type) => new(type, ValueState.Null, null);
    public static DynamicValue Unknown(TFType type) => new(type, ValueState.Unknown, null);

    public static DynamicValue String(string value) => Known(TFType.String, value);
    public static DynamicValue Number(TFNumber value) => Known(TFType.Number, value);
    public static DynamicValue Number(long value) => Number(TFNumber.FromInt64(value));
    public static DynamicValue Bool(bool value) => Known(TFType.Bool, value);

    public static DynamicValue List(TFType elementType, IEnumerable<DynamicValue> values) =>
        Known(new TFListType(elementType), values.ToArray());

    public static DynamicValue Set(TFType elementType, IEnumerable<DynamicValue> values) =>
        Known(new TFSetType(elementType), values.ToArray());

    public static DynamicValue Map(TFType elementType, IReadOnlyDictionary<string, DynamicValue> values) =>
        Known(new TFMapType(elementType), new Dictionary<string, DynamicValue>(values, StringComparer.Ordinal));

    public static DynamicValue Tuple(IReadOnlyList<TFType> elementTypes, IReadOnlyList<DynamicValue> values) =>
        Known(new TerraformTupleType(elementTypes), values.ToArray());

    public static DynamicValue Object(
        IReadOnlyDictionary<string, TFType> attributeTypes,
        IReadOnlyDictionary<string, DynamicValue> values) =>
        Known(new TerraformObjectType(attributeTypes), new Dictionary<string, DynamicValue>(values, StringComparer.Ordinal));

    public static DynamicValue Object(TerraformObjectType type, IReadOnlyDictionary<string, DynamicValue> values) =>
        Known(type, new Dictionary<string, DynamicValue>(values, StringComparer.Ordinal));

    public string AsString() => (string)RequireKnownValue(typeof(string));
    public TFNumber AsNumber() => (TFNumber)RequireKnownValue(typeof(TFNumber));
    public bool AsBoolean() => (bool)RequireKnownValue(typeof(bool));
    public IReadOnlyList<DynamicValue> AsSequence() => (IReadOnlyList<DynamicValue>)RequireKnownValue(typeof(IReadOnlyList<DynamicValue>));
    public IReadOnlyDictionary<string, DynamicValue> AsObject() => (IReadOnlyDictionary<string, DynamicValue>)RequireKnownValue(typeof(IReadOnlyDictionary<string, DynamicValue>));

    public DynamicValue GetAttribute(string attributeName)
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
