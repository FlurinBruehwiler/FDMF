using System;
using System.Threading.Tasks;
using FDMF.Core.Rpc;

namespace FDMF.Core.Generated;

public sealed class GeneratedServerProcedures(RpcEndpoint rpc) : global::FDMF.Core.IServerProcedures
{
    public Task<ServerStatus> GetStatus(int a, int b)
    {
        var guid = rpc.SendRequest(nameof(GetStatus), [ a, b ], false);
        return rpc.WaitForResponse<ServerStatus>(guid);
    }
}
