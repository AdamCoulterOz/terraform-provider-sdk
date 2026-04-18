namespace TerraformPluginDotnet.Hosting;

public static class TerraformPluginProtocol
{
    public const int CoreProtocolVersion = 1;
    public const int ApplicationProtocolVersion = 6;
    public const string MagicCookieKey = "TF_PLUGIN_MAGIC_COOKIE";
    public const string MagicCookieValue = "d602bf8f470bc67ca7faa0386276bbdd4330efaf76d1a219cb4d6991ca9872b2";
    public const string HealthServiceName = "plugin";
    public const string ClientCertificateEnvironmentVariable = "PLUGIN_CLIENT_CERT";
}
