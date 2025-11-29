using System.Net.WebSockets;
using System.Threading.Channels;
using Shared;

namespace Client;

public class ClientState
{
    public required Channel<Stream> MessagesToSend;
    public required Dictionary<Guid, PendingRequest> PendingRequests;
    public required IServerProcedures ServerProcedures;
}

public static class Connection
{
    public static ClientState CreateClientState()
    {
        var messages = Channel.CreateBounded<Stream>(100);
        var pendingRequests = new Dictionary<Guid, PendingRequest>();

        return new ClientState
        {
            MessagesToSend = messages,
            PendingRequests = pendingRequests,
            ServerProcedures = new GeneratedServerProcedures(messages, pendingRequests)
        };
    }

    public static async Task ConnectRemote(IClientProcedures clientProcedures, ClientState clientState)
    {
        var wsWrapper = new WebSocketWrapper();

        Helper.FireAndForget(SendMessages(clientState.MessagesToSend, wsWrapper));

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

            await NetworkingClient.ProcessMessagesForWebSocket(ws, clientState.MessagesToSend, clientProcedures, clientState.PendingRequests);

            wsWrapper.CurrentWebSocket = null;

            Console.WriteLine("Disconnected!");
        }
    }

    private static async Task SendMessages(Channel<Stream> messages, WebSocketWrapper webSocket)
    {
        await foreach (var message in messages.Reader.ReadAllAsync())
        {
            while (true)
            {
                if (webSocket.CurrentWebSocket is { State: WebSocketState.Open })
                {
                    try
                    {
                        message.Seek(0, SeekOrigin.Begin);
                        await using var stream = WebSocketStream.CreateWritableMessageStream(webSocket.CurrentWebSocket, WebSocketMessageType.Binary);
                        await message.CopyToAsync(stream);

                        break; //next message
                    }
                    catch (Exception e)
                    {
                        Logging.Log(LogFlags.Info, $"Connection closed {e.Message}");
                    }
                }
                else
                {
                    await Task.Delay(100);
                }
            }
        }
    }
}

public class WebSocketWrapper
{
    public WebSocket? CurrentWebSocket;
}