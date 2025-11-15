using Model;
using Networking;

namespace Server;

public class ServerProceduresImpl(ConnectedClient connectedClient) : IServerProcedures
{
    public Task<ServerStatus> GetStatus(int a, int b)
    {
        Logging.Log(LogFlags.Business, "Getting Status");

        for (int i = 0; i < 10; i++)
        {
            connectedClient.ClientProcedures.Ping();
        }

        return Task.FromResult(default(ServerStatus));
    }
}