using System.Net.WebSockets;

namespace Networking;

public class ClientProcedures(Action<Stream> sendMessage, Dictionary<Guid, PendingRequest> callbacks) : IClientProcedures
{
    public void Ping()
    {
        var guid = NetworkingClient.SendRequest(sendMessage, nameof(Ping), [  ], true);
    }
}
