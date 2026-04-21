namespace TerraformPlugin.Types;

public static class TF
{
    public static TF<T> Known<T>(T value) => TF<T>.Known(value);

    public static TF<T> Null<T>() => TF<T>.Null();

    public static TF<T> Unknown<T>() => TF<T>.Unknown();
}

public readonly struct TF<T>
{
    private TF(ValueState state, T? value)
    {
        State = state;
        Value = value;
    }

    public ValueState State { get; }

    public T? Value { get; }

    public bool IsKnown => State == ValueState.Known;

    public bool IsNull => State == ValueState.Null;

    public bool IsUnknown => State == ValueState.Unknown;

    public T RequireValue() =>
        IsKnown
            ? Value!
            : throw new InvalidOperationException("Terraform value is not known.");

    public T? GetValueOrDefault() => IsKnown ? Value : default;

    public static TF<T> Known(T value) => new(ValueState.Known, value);

    public static TF<T> Null() => new(ValueState.Null, default);

    public static TF<T> Unknown() => new(ValueState.Unknown, default);

    public static implicit operator TF<T>(T value) => Known(value);
}
