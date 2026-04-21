using Grpc.HealthCheck;
using Grpc.Health.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TerraformPluginDotnet.Provider;

namespace TerraformPluginDotnet.Hosting;

public static class TerraformProviderHost
{
    public static Task<int> RunAsync<TConfig, TProviderState>(
        TerraformProvider<TConfig, TProviderState> provider,
        string[] args,
        CancellationToken cancellationToken = default)
        where TConfig : new() =>
        RunAsync(provider.ToInternalProvider(), args, cancellationToken);

    internal static async Task<int> RunAsync(ITerraformProvider provider, string[] args, CancellationToken cancellationToken = default)
    {
        if (!IsPluginLaunch())
        {
            await Console.Error.WriteLineAsync(
                "This binary is a Terraform provider plugin. Run it through Terraform so the plugin handshake can complete.")
                .ConfigureAwait(false);
            return 1;
        }

        var builder = WebApplication.CreateBuilder(args);
        var healthService = new HealthServiceImpl();
        var mutualTls = TryCreateMutualTls();
        healthService.SetStatus(TerraformPluginProtocol.HealthServiceName, HealthCheckResponse.Types.ServingStatus.Serving);

        builder.WebHost.ConfigureKestrel(
            options =>
            {
                options.Listen(
                    IPAddress.Loopback,
                    0,
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

        var endpoint = GetBoundLoopbackEndpoint(app);

        var serverCertificate = mutualTls is null
            ? string.Empty
            : Convert.ToBase64String(mutualTls.ServerCertificate.RawData).TrimEnd('=');
        var handshakeLine = $"{TerraformPluginProtocol.CoreProtocolVersion}|{TerraformPluginProtocol.ApplicationProtocolVersion}|tcp|{endpoint.Address}:{endpoint.Port}|grpc|{serverCertificate}";
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

    private static PluginMutualTls? TryCreateMutualTls()
    {
        var clientCertificatePem = Environment.GetEnvironmentVariable(TerraformPluginProtocol.ClientCertificateEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(clientCertificatePem))
        {
            return null;
        }

        var clientCertificate = X509Certificate2.CreateFromPem(clientCertificatePem);
        var serverCertificate = CreateServerCertificate();
        return new PluginMutualTls(clientCertificate, serverCertificate);
    }

    internal static IPEndPoint GetBoundLoopbackEndpoint(WebApplication app)
    {
        var addressesFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel did not expose bound server addresses.");

        var loopbackAddress = addressesFeature.Addresses
            .Select(static address => new Uri(address))
            .FirstOrDefault(static uri => uri.Host == IPAddress.Loopback.ToString())
            ?? throw new InvalidOperationException("Could not determine the bound loopback server address.");

        return new IPEndPoint(IPAddress.Parse(loopbackAddress.Host), loopbackAddress.Port);
    }

    internal static X509Certificate2 CreateServerCertificate()
    {
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
        subjectAlternativeName.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(subjectAlternativeName.Build());

        // The certificate is per provider process and effectively ephemeral in practice,
        // so a long validity window avoids clock-skew surprises during local Terraform runs.
        var notBefore = DateTimeOffset.UtcNow.AddSeconds(-30);
        var notAfter = DateTimeOffset.UtcNow.AddHours(262980);
        return certificateRequest.CreateSelfSigned(notBefore, notAfter);
    }

    private sealed record PluginMutualTls(
        X509Certificate2 ClientCertificate,
        X509Certificate2 ServerCertificate);
}
