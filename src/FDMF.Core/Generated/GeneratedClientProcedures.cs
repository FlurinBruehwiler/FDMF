using System;
using System.Threading.Tasks;
using FDMF.Core.Rpc;

namespace FDMF.Core.Generated;

public sealed class GeneratedClientProcedures(RpcEndpoint rpc) : global::FDMF.Core.IClientProcedures
{
    public void Ping()
    {
        var guid = rpc.SendRequest(nameof(Ping), [  ], true);
    }
}
