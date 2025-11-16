using System.Net.WebSockets;
using Client;
using Model;
using Networking;

//todo, the goal is that the server and client can be in the same process!!!
//we still want to serialize / deserialize everything, so we get the exact same behaviour
//but we don't want a direct dependency on the WebSocket in the ServerProcedures

Logging.LogFlags = LogFlags.Error;

var ws = new ClientWebSocket();

Logging.Log(LogFlags.Info, "Trying to connect...");
await ws.ConnectAsync(new Uri("ws://localhost:8080/connect/"), CancellationToken.None);
Logging.Log(LogFlags.Info, "Connected!");

var semaphore = new SemaphoreSlim(1, 1);

Dictionary<Guid, PendingRequest> pendingRequests = [];
_ = NetworkingClient.ProcessMessagesForWebSocket(ws, semaphore, new ClientProceduresImpl(), pendingRequests).ContinueWith(x =>
{
    Logging.LogException(x.Exception);
}, TaskContinuationOptions.OnlyOnFaulted);

IServerProcedures serverProcedures = new ServerProcedures(x =>
{
    semaphore.Wait();
    try
    {
        x.Seek(0, SeekOrigin.Begin);
        using var stream = WebSocketStream.CreateWritableMessageStream(ws, WebSocketMessageType.Binary);
        x.CopyTo(stream);
    }
    catch (Exception e)
    {
        Logging.Log(LogFlags.Info, $"Connection closed {e.Message}");
    }
    finally
    {
        semaphore.Release();
    }
}, pendingRequests);

while (true)
{
    var res = await serverProcedures.GetStatus(1, 2);
    Logging.Log(LogFlags.Info, res.ToString());
}

namespace Client
{
    class ClientProceduresImpl : IClientProcedures
    {
        public void Ping()
        {
            Logging.Log(LogFlags.Info, "Got Ping");
        }
    }
}