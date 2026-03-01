using FDMF.Core.Rpc;
using FDMF.Tests.Generated;

namespace FDMF.Tests;

public sealed class GeneratedNetworkingProceduresTests
{
    [Fact]
    public async Task Generated_Proxy_Works_Over_Local_Transport()
    {
        var (aTransport, bTransport) = LocalFrameTransport.CreatePair();
        await using var _a = aTransport;
        await using var _b = bTransport;

        var server = new TestProceduresImpl();
        var serverEndpoint = new RpcEndpoint(aTransport, new GeneratedTestProceduresDispatcher(server));

        // Client doesn't need to handle incoming requests for this test.
        var clientEndpoint = new RpcEndpoint(bTransport, NullRpcDispatcher.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverLoop = serverEndpoint.RunAsync(cts.Token);
        var clientLoop = clientEndpoint.RunAsync(cts.Token);

        var client = new GeneratedTestProcedures(clientEndpoint);

        client.Ping();
        await SpinWaitUntil(() => server.PingCount == 1, cts.Token);

        var sum = await client.Add(5, 7);
        Assert.Equal(12, sum);

        await aTransport.DisposeAsync();
        await bTransport.DisposeAsync();
        await Task.WhenAll(serverLoop, clientLoop);
    }

    private static async Task SpinWaitUntil(Func<bool> predicate, CancellationToken cancellationToken)
    {
        while (!predicate())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(5, cancellationToken);
        }
    }

    private sealed class TestProceduresImpl : ITestProcedures
    {
        public int PingCount;

        public void Ping() => PingCount++;
        public Task<int> Add(int a, int b) => Task.FromResult(a + b);
    }
}
