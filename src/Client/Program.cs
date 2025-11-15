using System.Net.WebSockets;
using Client;
using Networking;

//todo, the goal is that the server and client can be in the same process!!!
//we still want to serialize / deserialize everything, so we get the exact same behaviour
//but we don't want a direct dependency on the WebSocket in the ServerProcedures

Console.WriteLine("Hello, World!");

var ws = new ClientWebSocket();
await ws.ConnectAsync(new Uri("ws://localhost:8080"), CancellationToken.None);
Console.WriteLine("Connected!");

Dictionary<Guid, PendingRequest> pendingRequests = [];
_ = NetworkingClient.ProcessMessagesForWebSocket(ws, new MessageHandler(), pendingRequests);

var serverProcedures = new ServerProcedures(x =>
{
    using var stream = WebSocketStream.CreateWritableMessageStream(ws, WebSocketMessageType.Binary);
    x.CopyTo(stream);
}, pendingRequests);

while (true)
{
    var res = await serverProcedures.GetStatus(1, 2);
    Console.WriteLine(res);
}

namespace Client
{
    class MessageHandler
    {

    }
}