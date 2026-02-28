using System.Net.WebSockets;
using System.Threading.Channels;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
using FDMF.Core.Generated;

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

            var messageHandler = new WebSocketMessageHandler(ws);

            await NetworkingClient.ProcessMessagesForWebSocket(ws, messageHandler, clientProcedures, callbacks);

            wsWrapper.CurrentWebSocket = null;

            Console.WriteLine("Disconnected!");
        }
    }
}

public sealed class WebSocketWrapper
{
    public WebSocket? CurrentWebSocket;
}