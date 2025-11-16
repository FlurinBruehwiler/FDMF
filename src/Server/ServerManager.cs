using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Channels;
using Model;
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

    private long lastMetricDump;
    public async Task LogMetrics()
    {
        while (true)
        {
            await Task.Delay(1000);

            var ellapsedSeconds = Stopwatch.GetElapsedTime(lastMetricDump).TotalSeconds;
            foreach (var (k, v) in Logging.metrics)
            {
                var metricsPerSecond = (double)v / ellapsedSeconds;
                Logging.Log(LogFlags.Performance, $"{k}: {metricsPerSecond} per Second");

                Logging.metrics[k] = 0;
            }

            lastMetricDump = Stopwatch.GetTimestamp();
        }
    }

    public async Task ListenForConnections()
    {
        var url = "http://localhost:8080/connect/";

        var listener = new HttpListener();
        listener.Prefixes.Add(url);
        listener.Start();

        Logging.Log(LogFlags.Info, $"Listening on {url}");

        while (true)
        {
            var context = await listener.GetContextAsync();
            if (context.Request.IsWebSocketRequest)
            {
                var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);

                Logging.Log(LogFlags.Info, "Client connected!");

                var messagesToSend = Channel.CreateBounded<Stream>(100);

                var connectedClient = new ConnectedClient
                {
                    ClientProcedures = new ClientProcedures(messagesToSend, Callbacks)
                };

                ConnectedClients.Add(connectedClient);

                _ = NetworkingClient.ProcessMessagesForWebSocket(wsContext.WebSocket, messagesToSend, new ServerProceduresImpl(connectedClient), Callbacks).ContinueWith(x =>
                {
                    if(x.Exception != null)
                        Logging.LogException(x.Exception);

                    ConnectedClients.Remove(connectedClient);
                });

                Helper.FireAndForget(SendMessages(messagesToSend, wsContext.WebSocket));
            }
            else
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
            }
        }
    }

    public static async Task SendMessages (Channel<Stream> messagesToSend, WebSocket ws)
    {
        await foreach (var message in messagesToSend.Reader.ReadAllAsync())
        {
            try
            {
                message.Seek(0, SeekOrigin.Begin);
                await using var stream = WebSocketStream.CreateWritableMessageStream(ws, WebSocketMessageType.Binary);
                await message.CopyToAsync(stream);
            }
            catch (Exception e)
            {
                Logging.Log(LogFlags.Info, $"Connection closed {e.Message}");
            }
        }
    }
}