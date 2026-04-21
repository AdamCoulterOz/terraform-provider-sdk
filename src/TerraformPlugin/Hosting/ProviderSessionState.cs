namespace TerraformPlugin.Hosting;

internal sealed class ProviderSessionState
{
    private object? _providerState;

    public object? ProviderState
    {
        get => Volatile.Read(ref _providerState);
        set => Volatile.Write(ref _providerState, value);
    }
}
