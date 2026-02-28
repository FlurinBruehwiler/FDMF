using System.Threading.Channels;

namespace FDMF.Core.Generated;

public sealed class GeneratedClientProcedures(IMessageHandler messageHandler, Dictionary<Guid, PendingRequest> callbacks) : IClientProcedures
{
    public void Ping()
    {
        var guid = NetworkingClient.SendRequest(messageHandler, nameof(Ping), [  ], true);
    }
}
