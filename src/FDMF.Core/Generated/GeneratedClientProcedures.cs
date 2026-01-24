using System.Threading.Channels;

namespace FDMF.Core.Generated;

public class GeneratedClientProcedures(Channel<Stream> sendMessage, Dictionary<Guid, PendingRequest> callbacks) : IClientProcedures
{
    public void Ping()
    {
        var guid = NetworkingClient.SendRequest(sendMessage, nameof(Ping), [  ], true);
    }
}
