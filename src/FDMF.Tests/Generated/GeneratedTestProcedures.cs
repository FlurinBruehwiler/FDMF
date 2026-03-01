// ReSharper disable All
using System;
using System.Threading.Tasks;
using FDMF.Core.Rpc;
using MemoryPack;

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

public sealed class GeneratedTestProceduresDispatcher(global::FDMF.Tests.ITestProcedures impl) : IRpcDispatcher
{
    public bool TryDispatch(RpcDecodedMessage msg, out Task<object?> task)
    {
        task = Task.FromResult<object?>(null);
        if (msg.MethodName is null) return false;
        var args = msg.ArgPayloads ?? Array.Empty<ReadOnlyMemory<byte>>();
        switch (msg.MethodName)
        {
            case nameof(global::FDMF.Tests.ITestProcedures.Ping):
            {
                if (args.Length != 0) return false;
                impl.Ping();
                task = Task.FromResult<object?>(null);
                return true;
            }
            case nameof(global::FDMF.Tests.ITestProcedures.Add):
            {
                if (args.Length != 2) return false;
                var p_a = MemoryPackSerializer.Deserialize<int>(args[0].Span, RpcCodec.SerializerOptions);
                var p_b = MemoryPackSerializer.Deserialize<int>(args[1].Span, RpcCodec.SerializerOptions);
                task = Wrap(impl.Add(p_a, p_b));
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
