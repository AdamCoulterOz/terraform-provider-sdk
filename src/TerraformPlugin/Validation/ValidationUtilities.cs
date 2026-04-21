using System.ComponentModel.DataAnnotations;
using System.Reflection;
using TerraformPlugin.Schema;
using TerraformPlugin.Types;

namespace TerraformPlugin.Validation;

public static class ValidationUtilities
{
    public static ValueState GetState(object? rawValue) =>
        TryGetWrappedValue(rawValue, out var state, out _)
            ? state
            : rawValue is null
                ? ValueState.Null
                : ValueState.Known;

    public static bool TryGetKnownValue<T>(object? rawValue, out T value)
    {
        var state = GetState(rawValue);

        if (state == ValueState.Known && GetKnownValue(rawValue) is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    public static bool TryGetProviderState<T>(ValidationContext context, out T providerState)
    {
        if (context.Items.TryGetValue(ValidationKeys.ProviderState, out var value) && value is T typed)
        {
            providerState = typed;
            return true;
        }

        providerState = default!;
        return false;
    }

    public static string GetSchemaMemberName(ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.MemberName))
        {
            return "value";
        }

        var member = context.ObjectType
            .GetMember(context.MemberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(static candidate => candidate.MemberType is MemberTypes.Property or MemberTypes.Field);

        return member is null
            ? context.MemberName
            : ModelConventions.GetSchemaMemberName(member);
    }

    private static object? GetKnownValue(object? rawValue)
    {
        if (TryGetWrappedValue(rawValue, out var state, out var value) && state == ValueState.Known)
        {
            return value;
        }

        return rawValue;
    }

    private static bool TryGetWrappedValue(object? rawValue, out ValueState state, out object? value)
    {
        if (rawValue is null)
        {
            state = ValueState.Null;
            value = null;
            return false;
        }

        var valueType = rawValue.GetType();

        if (!valueType.IsGenericType || valueType.GetGenericTypeDefinition() != typeof(TF<>))
        {
            state = default;
            value = null;
            return false;
        }

        state = (ValueState)(valueType.GetProperty(nameof(TF<>.State))!.GetValue(rawValue)
            ?? throw new InvalidOperationException("Could not read Terraform value state."));
        value = state == ValueState.Known
            ? valueType.GetMethod(nameof(TF<>.RequireValue))!.Invoke(rawValue, [])
            : null;

        return true;
    }
}
