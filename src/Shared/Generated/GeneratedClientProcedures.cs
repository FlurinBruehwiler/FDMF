using System.Threading.Channels;

namespace Shared;

public class GeneratedClientProcedures(Channel<Stream> sendMessage, Dictionary<Guid, PendingRequest> callbacks) : IClientProcedures
{
    public void Ping()
    {
        var guid = NetworkingClient.SendRequest(sendMessage, nameof(Ping), [  ], true);
    }
}
