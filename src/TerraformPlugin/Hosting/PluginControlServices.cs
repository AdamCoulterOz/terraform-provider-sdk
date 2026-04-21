using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Plugin;
using ProtobufEmpty = Google.Protobuf.WellKnownTypes.Empty;
using PluginEmpty = Plugin.Empty;

namespace TerraformPlugin.Hosting;

internal sealed class PluginGrpcControllerService(IHostApplicationLifetime applicationLifetime) : GRPCController.GRPCControllerBase
{
    public override Task<PluginEmpty> Shutdown(PluginEmpty request, ServerCallContext context)
    {
        applicationLifetime.StopApplication();
        return Task.FromResult(new PluginEmpty());
    }
}

internal sealed class PluginGrpcBrokerService : GRPCBroker.GRPCBrokerBase
{
    public override async Task StartStream(
        IAsyncStreamReader<ConnInfo> requestStream,
        IServerStreamWriter<ConnInfo> responseStream,
        ServerCallContext context)
    {
        try
        {
            while (await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
            {
                // The focused SDK does not currently broker nested gRPC services.
                // We still keep the stream alive for go-plugin compatibility.
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }
}

internal sealed class PluginGrpcStdioService : GRPCStdio.GRPCStdioBase
{
    public override Task StreamStdio(ProtobufEmpty request, IServerStreamWriter<StdioData> responseStream, ServerCallContext context) =>
        Task.CompletedTask;
}
