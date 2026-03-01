using System.IO.Pipes;
using FDMF.Core;
using FDMF.Core.Generated;
using FDMF.Core.Rpc;
using FDMF.Server;

Logging.LogFlags = LogFlags.Info | LogFlags.Error;

var serverManager = new ServerManager();
_ = serverManager.ListenForConnections();

// Local plugin IPC (duplex) demo.
_ = RunPluginPipeHost();

await Task.Delay(Timeout.Infinite);

static async Task RunPluginPipeHost()
{
    while (true)
    {
        await using var pipe = new NamedPipeServerStream(
            "FDMF.Plugin",
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await pipe.WaitForConnectionAsync();

        var transport = new NamedPipeFrameTransport(pipe);
        var hostHandler = new HostProceduresImpl();
        var endpoint = new RpcEndpoint(transport, hostHandler);
        var plugin = new GeneratedPluginProcedures(endpoint);
        _ = endpoint.RunAsync();

        // Exercise duplex.
        var sum = await plugin.Add(1, 2);
        Logging.Log(LogFlags.Business, $"Plugin Add(1,2) = {sum}");
    }
}

sealed class HostProceduresImpl : IHostProcedures
{
    public void Ping()
    {
        Logging.Log(LogFlags.Business, "Plugin pinged host");
    }

    public Task<string> Echo(string msg)
    {
        return Task.FromResult(msg);
    }
}
