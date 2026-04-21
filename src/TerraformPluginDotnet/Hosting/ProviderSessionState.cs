namespace TerraformPluginDotnet.Hosting;

internal sealed class ProviderSessionState
{
    private object? _providerState;

    public object? ProviderState
    {
        get => System.Threading.Volatile.Read(ref _providerState);
        set => System.Threading.Volatile.Write(ref _providerState, value);
    }
}
