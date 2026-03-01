using FDMF.Core;
using FDMF.Core.Generated;
using FDMF.Core.Rpc;

namespace FDMF.Tests;

public sealed class LocalRpcTransportTests
{
    [Fact]
    public async Task Duplex_Rpc_Works_In_Process()
    {
        var (aTransport, bTransport) = LocalFrameTransport.CreatePair();
        await using var _a = aTransport;
        await using var _b = bTransport;

        var hostHandler = new HostHandler();
        var pluginHandler = new PluginHandler();

        var hostEndpoint = new RpcEndpoint(aTransport, hostHandler);
        var pluginEndpoint = new RpcEndpoint(bTransport, pluginHandler);

        var hostToPlugin = new GeneratedPluginProcedures(hostEndpoint);
        var pluginToHost = new GeneratedHostProcedures(pluginEndpoint);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var hostLoop = hostEndpoint.RunAsync(cts.Token);
        var pluginLoop = pluginEndpoint.RunAsync(cts.Token);

        await Task.WhenAll(hostEndpoint.Connected, pluginEndpoint.Connected);

        // Host -> Plugin request/response
        var sum = await hostToPlugin.Add(10, 32);
        Assert.Equal(42, sum);

        // Plugin -> Host notification
        pluginToHost.Ping();
        await SpinWaitUntil(() => hostHandler.PingCount == 1, cts.Token);

        // Plugin -> Host request/response
        var echo = await pluginToHost.Echo("hi");
        Assert.Equal("hi", echo);

        await aTransport.DisposeAsync();
        await bTransport.DisposeAsync();

        await Task.WhenAll(hostLoop, pluginLoop);
    }

    private static async Task SpinWaitUntil(Func<bool> predicate, CancellationToken cancellationToken)
    {
        while (!predicate())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(5, cancellationToken);
        }
    }

    private sealed class HostHandler : IHostProcedures
    {
        public int PingCount;

        public void Ping() => PingCount++;
        public Task<string> Echo(string msg) => Task.FromResult(msg);
    }

    private sealed class PluginHandler : IPluginProcedures
    {
        public Task<int> Add(int a, int b) => Task.FromResult(a + b);
    }
}
