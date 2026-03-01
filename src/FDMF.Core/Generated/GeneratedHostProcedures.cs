// ReSharper disable All
using System;
using System.Threading.Tasks;
using FDMF.Core.Rpc;
using MemoryPack;

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

public sealed class GeneratedHostProceduresDispatcher(global::FDMF.Core.IHostProcedures impl) : IRpcDispatcher
{
    public bool TryDispatch(RpcDecodedMessage msg, out Task<object?> task)
    {
        task = Task.FromResult<object?>(null);
        if (msg.MethodName is null) return false;
        var args = msg.ArgPayloads ?? Array.Empty<ReadOnlyMemory<byte>>();
        switch (msg.MethodName)
        {
            case nameof(global::FDMF.Core.IHostProcedures.Ping):
            {
                if (args.Length != 0) return false;
                impl.Ping();
                task = Task.FromResult<object?>(null);
                return true;
            }
            case nameof(global::FDMF.Core.IHostProcedures.Echo):
            {
                if (args.Length != 1) return false;
                var p_msg = MemoryPackSerializer.Deserialize<string>(args[0].Span, RpcCodec.SerializerOptions);
                task = Wrap(impl.Echo(p_msg));
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
