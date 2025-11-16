using System.Threading.Channels;

namespace Networking;

public class ClientProcedures(Channel<Stream> sendMessage, Dictionary<Guid, PendingRequest> callbacks) : IClientProcedures
{
    public void Ping()
    {
        var guid = NetworkingClient.SendRequest(sendMessage, nameof(Ping), [  ], true);
    }
}
