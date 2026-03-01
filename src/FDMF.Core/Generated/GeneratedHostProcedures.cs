using System;
using System.Threading.Tasks;
using FDMF.Core.Rpc;

namespace FDMF.Core.Generated;

public sealed class GeneratedHostProcedures(RpcEndpoint rpc) : global::FDMF.Core.IHostProcedures
{
    public void Ping()
    {
        var guid = rpc.SendRequest(nameof(Ping), [  ], true);
    }
    public Task<string> Echo(string msg)
    {
        var guid = rpc.SendRequest(nameof(Echo), [ msg ], false);
        return rpc.WaitForResponse<string>(guid);
    }
}
