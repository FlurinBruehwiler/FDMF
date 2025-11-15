using System.Net;
using System.Net.WebSockets;
using Networking;

namespace Server;

public class ConnectedClient
{
    public required IClientProcedures ClientProcedures;
}

public class ServerManager
{
    public List<ConnectedClient> ConnectedClients = [];
    public Dictionary<Guid, PendingRequest> Callbacks = [];

    public async Task ListenForConnections()
    {
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:8080/connect");
        listener.Start();

        while (true)
        {
            var context = await listener.GetContextAsync();
            if (context.Request.IsWebSocketRequest)
            {
                var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);

                var connectedClient = new ConnectedClient
                {
                    ClientProcedures = new ClientProcedures(x =>
                    {
                        using var stream = WebSocketStream.CreateWritableMessageStream(wsContext.WebSocket, WebSocketMessageType.Binary);
                        x.CopyTo(stream);
                    }, Callbacks)
                };

                ConnectedClients.Add(connectedClient);

                _ = NetworkingClient.ProcessMessagesForWebSocket(wsContext.WebSocket, new ServerProceduresImpl(connectedClient), Callbacks);
            }
            else
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
            }
        }
    }
}