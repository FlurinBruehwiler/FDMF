using FDMF.Core.Rpc;

namespace FDMF.Tests;

public sealed class RpcHandshakeTests
{
    [Fact]
    public async Task Mismatched_Version_Fails_Handshake()
    {
        var (aTransport, bTransport) = LocalFrameTransport.CreatePair();
        await using var _a = aTransport;
        await using var _b = bTransport;

        var a = new RpcEndpoint(aTransport, NullRpcDispatcher.Instance, protocolVersion: 1);
        var b = new RpcEndpoint(bTransport, NullRpcDispatcher.Instance, protocolVersion: 2);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var ta = a.RunAsync(cts.Token);
        var tb = b.RunAsync(cts.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Task.WhenAll(ta, tb));
    }
}
