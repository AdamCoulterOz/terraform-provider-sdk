namespace TerraformPluginDotnet.Types;

public static class TF
{
    public static TF<T> Known<T>(T value) => TF<T>.Known(value);

    public static TF<T> Null<T>() => TF<T>.Null();

    public static TF<T> Unknown<T>() => TF<T>.Unknown();
}

public readonly struct TF<T>
{
    private TF(TerraformValueState state, T? value)
    {
        State = state;
        Value = value;
    }

    public TerraformValueState State { get; }

    public T? Value { get; }

    public bool IsKnown => State == TerraformValueState.Known;

    public bool IsNull => State == TerraformValueState.Null;

    public bool IsUnknown => State == TerraformValueState.Unknown;

    public T RequireValue() =>
        IsKnown
            ? Value!
            : throw new InvalidOperationException("Terraform value is not known.");

    public T? GetValueOrDefault() => IsKnown ? Value : default;

    public static TF<T> Known(T value) => new(TerraformValueState.Known, value);

    public static TF<T> Null() => new(TerraformValueState.Null, default);

    public static TF<T> Unknown() => new(TerraformValueState.Unknown, default);

    public static implicit operator TF<T>(T value) => Known(value);
}
