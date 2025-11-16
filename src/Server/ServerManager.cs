using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using Model;
using Networking;

namespace Server;

public class ConnectedClient
{
    public required IClientProcedures ClientProcedures;
    public required SemaphoreSlim WebsocketSendSemaphore;
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

                var semaphore = new SemaphoreSlim(1, 1);
                var connectedClient = new ConnectedClient
                {
                    WebsocketSendSemaphore = semaphore,
                    ClientProcedures = new ClientProcedures(x =>
                    {
                        semaphore.Wait();
                        try
                        {
                            x.Seek(0, SeekOrigin.Begin);
                            using var stream = WebSocketStream.CreateWritableMessageStream(wsContext.WebSocket, WebSocketMessageType.Binary);
                            x.CopyTo(stream);
                        }
                        catch (Exception e)
                        {
                            Logging.LogException(e);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, Callbacks)
                };

                ConnectedClients.Add(connectedClient);

                _ = NetworkingClient.ProcessMessagesForWebSocket(wsContext.WebSocket, semaphore, new ServerProceduresImpl(connectedClient), Callbacks).ContinueWith(x =>
                {
                    if(x.Exception != null)
                        Logging.LogException(x.Exception);

                    ConnectedClients.Remove(connectedClient);
                });;
            }
            else
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
            }
        }
    }
}