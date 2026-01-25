using System.Threading.Channels;

namespace FDMF.Core.Generated;

public sealed class GeneratedServerProcedures(Channel<Stream> sendMessage, Dictionary<Guid, PendingRequest> callbacks) : IServerProcedures
{
    public Task<ServerStatus> GetStatus(int a, int b)
    {
        var guid = NetworkingClient.SendRequest(sendMessage, nameof(GetStatus), [ a, b ], false);
        return NetworkingClient.WaitForResponse<ServerStatus>(callbacks, guid);
    }
}
