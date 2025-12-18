using Model.Generated;
using Shared;

namespace Server;

public class ServerProceduresImpl(ConnectedClient connectedClient) : IServerProcedures
{
    public Task<ServerStatus> GetStatus(int a, int b)
    {
        Logging.Log(LogFlags.Business, "Getting Status");
        return Task.FromResult(default(ServerStatus));
    }

    public Task<Folder> GetSomeObjs()
    {
        var folder = new Folder();

        return Task.FromResult(folder);
    }
}