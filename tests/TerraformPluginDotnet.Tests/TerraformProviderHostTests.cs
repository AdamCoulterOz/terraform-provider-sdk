using TerraformPluginDotnet.Hosting;

namespace TerraformPluginDotnet.Tests;

public sealed class TerraformProviderHostTests
{
    [Fact]
    public void CreateServerCertificate_IncludesLoopbackDnsAndIpSans()
    {
        using var certificate = TerraformProviderHost.CreateServerCertificate();
        var subjectAlternativeName = certificate.Extensions["2.5.29.17"];

        Assert.NotNull(subjectAlternativeName);

        var formatted = subjectAlternativeName!.Format(multiLine: false);
        Assert.Contains("localhost", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("127.0.0.1", formatted, StringComparison.Ordinal);
    }
}
