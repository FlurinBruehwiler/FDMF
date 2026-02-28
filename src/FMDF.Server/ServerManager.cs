using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Channels;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
using FDMF.Core.Generated;

namespace FDMF.Server;

public sealed class ConnectedClient
{
    public required IClientProcedures ClientProcedures;
}


public sealed class ServerManager
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

                Logging.Log(LogFlags.Info, "FDMF.Client connected!");

                var handler = new WebSocketMessageHandler(wsContext.WebSocket);

                var connectedClient = new ConnectedClient
                {
                    ClientProcedures = new GeneratedClientProcedures(handler, Callbacks)
                };

                ConnectedClients.Add(connectedClient);

                _ = NetworkingClient.ProcessMessagesForWebSocket(wsContext.WebSocket, handler, new ServerProceduresImpl(connectedClient), Callbacks).ContinueWith(x =>
                {
                    if(x.Exception != null)
                        Logging.LogException(x.Exception);

                    ConnectedClients.Remove(connectedClient);
                });
            }
            else
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
            }
        }
    }
}