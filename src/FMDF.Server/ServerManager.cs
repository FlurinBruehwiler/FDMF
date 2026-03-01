using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
using FDMF.Core.Generated;
using FDMF.Core.Rpc;

namespace FDMF.Server;

public sealed class ConnectedClient
{
    public required IClientProcedures ClientProcedures;
    public required RpcEndpoint Rpc;
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

                var transport = new WebSocketFrameTransport(wsContext.WebSocket);

                var connectedClient = new ConnectedClient
                {
                    Rpc = null!,
                    ClientProcedures = null!,
                };
                connectedClient.Rpc = new RpcEndpoint(transport, new ServerProceduresImpl(connectedClient));
                connectedClient.ClientProcedures = new GeneratedClientProcedures(connectedClient.Rpc);

                ConnectedClients.Add(connectedClient);

                _ = connectedClient.Rpc.RunAsync().ContinueWith(x =>
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
