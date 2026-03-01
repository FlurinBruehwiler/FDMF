using System;
using System.Threading.Tasks;
using FDMF.Core.Rpc;

namespace FDMF.Tests.Generated;

public sealed class GeneratedTestProcedures(RpcEndpoint rpc) : global::FDMF.Tests.ITestProcedures
{
    public void Ping()
    {
        var guid = rpc.SendRequest(nameof(Ping), [  ], true);
    }
    public Task<int> Add(int a, int b)
    {
        var guid = rpc.SendRequest(nameof(Add), [ a, b ], false);
        return rpc.WaitForResponse<int>(guid);
    }
}
