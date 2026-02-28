using System.Threading.Channels;

namespace FDMF.Core.Generated;

public sealed class GeneratedServerProcedures(IMessageHandler handler, Dictionary<Guid, PendingRequest> callbacks) : IServerProcedures
{
    public Task<ServerStatus> GetStatus(int a, int b)
    {
        var guid = NetworkingClient.SendRequest(handler, nameof(GetStatus), [ a, b ], false);
        return NetworkingClient.WaitForResponse<ServerStatus>(callbacks, guid);
    }
}
