using TerraformPlugin.Diagnostics;

namespace TerraformPlugin.Provider;

internal static class RuntimeDiagnostics
{
    public static bool ShouldRethrow(Exception exception) =>
        exception is OperationCanceledException;

    public static IReadOnlyList<Diagnostic> FromException(string summary, Exception exception)
    {
        var unwrapped = Unwrap(exception);
        var detail = string.IsNullOrWhiteSpace(unwrapped.Message)
            ? unwrapped.GetType().Name
            : $"{unwrapped.GetType().Name}: {unwrapped.Message}";

        return
        [
            Diagnostic.Error(summary, detail),
        ];
    }

    private static Exception Unwrap(Exception exception) =>
        exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1
            ? Unwrap(aggregate.InnerExceptions[0])
            : exception is System.Reflection.TargetInvocationException target && target.InnerException is not null
                ? Unwrap(target.InnerException)
                : exception;
}
