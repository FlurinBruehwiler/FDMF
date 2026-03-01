// ReSharper disable All
using System;
using System.Threading.Tasks;
using FDMF.Core.Rpc;
using MemoryPack;

namespace FDMF.Core.Generated;

public sealed class GeneratedClientProcedures(RpcEndpoint rpc) : global::FDMF.Core.IClientProcedures
{
    public void Ping()
    {
        var guid = rpc.SendRequest(nameof(Ping), [  ], true);
    }
}

public sealed class GeneratedClientProceduresDispatcher(global::FDMF.Core.IClientProcedures impl) : IRpcDispatcher
{
    public bool TryDispatch(RpcDecodedMessage msg, out Task<object?> task)
    {
        task = Task.FromResult<object?>(null);
        if (msg.MethodName is null) return false;
        var args = msg.ArgPayloads ?? Array.Empty<ReadOnlyMemory<byte>>();
        switch (msg.MethodName)
        {
            case nameof(global::FDMF.Core.IClientProcedures.Ping):
            {
                if (args.Length != 0) return false;
                impl.Ping();
                task = Task.FromResult<object?>(null);
                return true;
            }
            default:
                return false;
        }
    }

    private static async Task<object?> Wrap<T>(Task<T> t)
    {
        var r = await t.ConfigureAwait(false);
        return r;
    }
}
