using System.IO.Pipes;
using FDMF.Core;
using FDMF.Core.Generated;
using FDMF.Core.Rpc;

Logging.LogFlags = LogFlags.Info | LogFlags.Error;

await using var pipe = new NamedPipeClientStream(
    ".",
    "FDMF.Plugin",
    PipeDirection.InOut,
    PipeOptions.Asynchronous);

await pipe.ConnectAsync();

var transport = new NamedPipeFrameTransport(pipe);
var handler = new PluginProceduresImpl();
var dispatcher = new GeneratedPluginProceduresDispatcher(handler);
var endpoint = new RpcEndpoint(transport, dispatcher);

var host = new GeneratedHostProcedures(endpoint);

_ = endpoint.RunAsync();

host.Ping();
var echo = await host.Echo("hello from plugin");
Logging.Log(LogFlags.Business, $"Host Echo -> {echo}");

await Task.Delay(Timeout.Infinite);

sealed class PluginProceduresImpl : IPluginProcedures
{
    public Task<int> Add(int a, int b)
    {
        Logging.Log(LogFlags.Business, $"Add({a},{b})");
        return Task.FromResult(a + b);
    }
}
