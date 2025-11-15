using System.Net.WebSockets;

namespace Networking;

public class ServerProcedures(Action<Stream> sendMessage, Dictionary<Guid, PendingRequest> callbacks) : IServerProcedures
{
    public Task<ServerStatus> GetStatus(int a, int b)
    {
        var guid = NetworkingClient.SendRequest(sendMessage, nameof(GetStatus), [ a, b ], false);
        return NetworkingClient.WaitForResponse<ServerStatus>(callbacks, guid);
    }
}
