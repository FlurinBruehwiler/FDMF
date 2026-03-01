using System;
using System.Threading.Tasks;
using FDMF.Core.Rpc;

namespace FDMF.Core.Generated;

public sealed class GeneratedPluginProcedures(RpcEndpoint rpc) : global::FDMF.Core.IPluginProcedures
{
    public Task<int> Add(int a, int b)
    {
        var guid = rpc.SendRequest(nameof(Add), [ a, b ], false);
        return rpc.WaitForResponse<int>(guid);
    }
}
