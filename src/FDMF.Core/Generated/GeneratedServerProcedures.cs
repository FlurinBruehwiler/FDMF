using System;
using System.Threading.Tasks;
using FDMF.Core.Rpc;
using MemoryPack;

namespace FDMF.Core.Generated;

public sealed class GeneratedServerProcedures(RpcEndpoint rpc) : global::FDMF.Core.IServerProcedures
{
    public Task<ServerStatus> GetStatus(int a, int b)
    {
        var guid = rpc.SendRequest(nameof(GetStatus), [ a, b ], false);
        return rpc.WaitForResponse<ServerStatus>(guid);
    }
}

public sealed class GeneratedServerProceduresDispatcher(global::FDMF.Core.IServerProcedures impl) : IRpcDispatcher
{
    public bool TryDispatch(RpcDecodedMessage msg, out Task<object?> task)
    {
        task = Task.FromResult<object?>(null);
        if (msg.MethodName is null) return false;
        var args = msg.ArgPayloads ?? Array.Empty<ReadOnlyMemory<byte>>();
        switch (msg.MethodName)
        {
            case nameof(global::FDMF.Core.IServerProcedures.GetStatus):
            {
                if (args.Length != 2) return false;
                var p_a = MemoryPackSerializer.Deserialize<int>(args[0].Span, RpcCodec.SerializerOptions);
                var p_b = MemoryPackSerializer.Deserialize<int>(args[1].Span, RpcCodec.SerializerOptions);
                task = Wrap(impl.GetStatus(p_a, p_b));
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
