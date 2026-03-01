using System.Net.WebSockets;
using FDMF.Core;
using FDMF.Core.Generated;
using FDMF.Core.Rpc;

namespace FDMF.Client;

public static class Connection
{
    public static async Task ConnectRemote(IClientProcedures clientProcedures, Dictionary<Guid,PendingRequest> callbacks)
    {
        var wsWrapper = new WebSocketWrapper();

        while (true)
        {
            var ws = new ClientWebSocket();
            Logging.Log(LogFlags.Info, "Trying to connect...");

            try
            {
                await ws.ConnectAsync(new Uri("ws://localhost:8080/connect/"), CancellationToken.None);
            }
            catch
            {
                continue;
            }

            Logging.Log(LogFlags.Info, "Connected!");
            wsWrapper.CurrentWebSocket = ws;

            var transport = new WebSocketFrameTransport(ws);
            var endpoint = new RpcEndpoint(transport, clientProcedures);

            // Remote server proxy (example usage, caller can keep reference).
            _ = new GeneratedServerProcedures(endpoint);

            await endpoint.RunAsync();

            wsWrapper.CurrentWebSocket = null;

            Console.WriteLine("Disconnected!");
        }
    }
}

public sealed class WebSocketWrapper
{
    public WebSocket? CurrentWebSocket;
}
