namespace TerraformPluginDotnet.Types;

public enum TerraformValueState
{
    Known,
    Null,
    Unknown,
}

public sealed class TerraformValue
{
    private TerraformValue(TerraformType type, TerraformValueState state, object? value)
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

    public static TerraformValue Known(TerraformType type, object value) => new(type, TerraformValueState.Known, value);
    public static TerraformValue Null(TerraformType type) => new(type, TerraformValueState.Null, null);
    public static TerraformValue Unknown(TerraformType type) => new(type, TerraformValueState.Unknown, null);

    public static TerraformValue String(string value) => Known(TerraformType.String, value);
    public static TerraformValue Number(TerraformNumber value) => Known(TerraformType.Number, value);
    public static TerraformValue Number(long value) => Number(TerraformNumber.FromInt64(value));
    public static TerraformValue Bool(bool value) => Known(TerraformType.Bool, value);

    public static TerraformValue List(TerraformType elementType, IEnumerable<TerraformValue> values) =>
        Known(new TerraformListType(elementType), values.ToArray());

    public static TerraformValue Set(TerraformType elementType, IEnumerable<TerraformValue> values) =>
        Known(new TerraformSetType(elementType), values.ToArray());

    public static TerraformValue Map(TerraformType elementType, IReadOnlyDictionary<string, TerraformValue> values) =>
        Known(new TerraformMapType(elementType), new Dictionary<string, TerraformValue>(values, StringComparer.Ordinal));

    public static TerraformValue Tuple(IReadOnlyList<TerraformType> elementTypes, IReadOnlyList<TerraformValue> values) =>
        Known(new TerraformTupleType(elementTypes), values.ToArray());

    public static TerraformValue Object(
        IReadOnlyDictionary<string, TerraformType> attributeTypes,
        IReadOnlyDictionary<string, TerraformValue> values) =>
        Known(new TerraformObjectType(attributeTypes), new Dictionary<string, TerraformValue>(values, StringComparer.Ordinal));

    public static TerraformValue Object(TerraformObjectType type, IReadOnlyDictionary<string, TerraformValue> values) =>
        Known(type, new Dictionary<string, TerraformValue>(values, StringComparer.Ordinal));

    public string AsString() => (string)RequireKnownValue(typeof(string));
    public TerraformNumber AsNumber() => (TerraformNumber)RequireKnownValue(typeof(TerraformNumber));
    public bool AsBoolean() => (bool)RequireKnownValue(typeof(bool));
    public IReadOnlyList<TerraformValue> AsSequence() => (IReadOnlyList<TerraformValue>)RequireKnownValue(typeof(IReadOnlyList<TerraformValue>));
    public IReadOnlyDictionary<string, TerraformValue> AsObject() => (IReadOnlyDictionary<string, TerraformValue>)RequireKnownValue(typeof(IReadOnlyDictionary<string, TerraformValue>));

    public TerraformValue GetAttribute(string attributeName)
    {
        var attributes = AsObject();

        if (!attributes.TryGetValue(attributeName, out var value))
        {
            throw new KeyNotFoundException($"Terraform object does not contain attribute '{attributeName}'.");
        }

        return value;
    }

    public string? GetOptionalString(string attributeName)
    {
        var value = GetAttribute(attributeName);

        if (value.IsNull || value.IsUnknown)
        {
            return null;
        }

        return value.AsString();
    }

    private object RequireKnownValue(Type expectedType)
    {
        if (!IsKnown || Value is null)
        {
            throw new InvalidOperationException("Terraform value is not known.");
        }

        if (!expectedType.IsAssignableFrom(Value.GetType()))
        {
            throw new InvalidOperationException($"Terraform value is '{Value.GetType().Name}', expected '{expectedType.Name}'.");
        }

        return Value;
    }
}
