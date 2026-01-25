using FDMF.Core;

namespace FDMF.Server;

public sealed class ServerProceduresImpl(ConnectedClient connectedClient) : IServerProcedures
{
    public Task<ServerStatus> GetStatus(int a, int b)
    {
        Logging.Log(LogFlags.Business, "Getting Status");
        return Task.FromResult(default(ServerStatus));
    }

}