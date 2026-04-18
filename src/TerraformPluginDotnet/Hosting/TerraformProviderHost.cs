using Grpc.HealthCheck;
using Grpc.Health.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TerraformPluginDotnet.Provider;

namespace TerraformPluginDotnet.Hosting;

public static class TerraformProviderHost
{
    public static async Task<int> RunAsync(ITerraformProvider provider, string[] args, CancellationToken cancellationToken = default)
    {
        if (!IsPluginLaunch())
        {
            await Console.Error.WriteLineAsync(
                "This binary is a Terraform provider plugin. Run it through Terraform so the plugin handshake can complete.")
                .ConfigureAwait(false);
            return 1;
        }

        var port = ReserveTcpPort();
        var builder = WebApplication.CreateBuilder(args);
        var healthService = new HealthServiceImpl();
        var mutualTls = TryCreateMutualTls();
        healthService.SetStatus(TerraformPluginProtocol.HealthServiceName, HealthCheckResponse.Types.ServingStatus.Serving);

        builder.WebHost.ConfigureKestrel(
            options =>
            {
                options.ListenLocalhost(
                    port,
                    listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http2;

                        if (mutualTls is not null)
                        {
                            listenOptions.UseHttps(
                                new HttpsConnectionAdapterOptions
                                {
                                    ServerCertificate = mutualTls.ServerCertificate,
                                    ClientCertificateMode = ClientCertificateMode.RequireCertificate,
                                    SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                                    ClientCertificateValidation = (certificate, _, _) =>
                                        certificate is not null &&
                                        certificate.RawData.AsSpan().SequenceEqual(mutualTls.ClientCertificate.RawData),
                                });
                        }
                    });
                options.Limits.MaxRequestBodySize = int.MaxValue;
            });

        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(provider);
        builder.Services.AddSingleton<ProviderSessionState>();
        builder.Services.AddGrpc(
            options =>
            {
                options.MaxReceiveMessageSize = int.MaxValue;
                options.MaxSendMessageSize = int.MaxValue;
                options.EnableDetailedErrors = true;
            });
        builder.Services.AddSingleton(healthService);

        var app = builder.Build();

        app.MapGrpcService<ProviderRpcService>();
        app.MapGrpcService<PluginGrpcControllerService>();
        app.MapGrpcService<PluginGrpcBrokerService>();
        app.MapGrpcService<PluginGrpcStdioService>();
        app.MapGrpcService<HealthServiceImpl>();

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var serverCertificate = mutualTls is null
            ? string.Empty
            : Convert.ToBase64String(mutualTls.ServerCertificate.RawData).TrimEnd('=');
        var handshakeLine = $"{TerraformPluginProtocol.CoreProtocolVersion}|{TerraformPluginProtocol.ApplicationProtocolVersion}|tcp|127.0.0.1:{port}|grpc|{serverCertificate}";
        Console.Out.WriteLine(handshakeLine);
        Console.Out.Flush();
        Console.SetOut(TextWriter.Null);

        await app.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static bool IsPluginLaunch() =>
        string.Equals(
            Environment.GetEnvironmentVariable(TerraformPluginProtocol.MagicCookieKey),
            TerraformPluginProtocol.MagicCookieValue,
            StringComparison.Ordinal);

    private static int ReserveTcpPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static PluginMutualTls? TryCreateMutualTls()
    {
        var clientCertificatePem = Environment.GetEnvironmentVariable(TerraformPluginProtocol.ClientCertificateEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(clientCertificatePem))
        {
            return null;
        }

        var clientCertificate = X509Certificate2.CreateFromPem(clientCertificatePem);
        using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP521);
        var certificateRequest = new CertificateRequest(
            "CN=localhost",
            serverKey,
            HashAlgorithmName.SHA512);

        certificateRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, false, 0, true));
        certificateRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature |
                X509KeyUsageFlags.KeyEncipherment |
                X509KeyUsageFlags.KeyAgreement |
                X509KeyUsageFlags.KeyCertSign,
                true));
        certificateRequest.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [
                    new Oid("1.3.6.1.5.5.7.3.1"),
                    new Oid("1.3.6.1.5.5.7.3.2"),
                ],
                true));

        var subjectAlternativeName = new SubjectAlternativeNameBuilder();
        subjectAlternativeName.AddDnsName("localhost");
        certificateRequest.CertificateExtensions.Add(subjectAlternativeName.Build());

        var notBefore = DateTimeOffset.UtcNow.AddSeconds(-30);
        var notAfter = DateTimeOffset.UtcNow.AddHours(262980);
        var serialNumber = RandomNumberGenerator.GetBytes(16);
        var serverCertificate = certificateRequest.CreateSelfSigned(notBefore, notAfter);

        return new PluginMutualTls(clientCertificate, serverCertificate);
    }

    private sealed record PluginMutualTls(
        X509Certificate2 ClientCertificate,
        X509Certificate2 ServerCertificate);
}
