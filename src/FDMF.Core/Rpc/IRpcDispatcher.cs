namespace FDMF.Core.Rpc;

public interface IRpcDispatcher
{
    bool TryDispatch(RpcDecodedMessage msg, out Task<object?> task);
}

public sealed class NullRpcDispatcher : IRpcDispatcher
{
    public static readonly NullRpcDispatcher Instance = new();

    private NullRpcDispatcher() { }

    public bool TryDispatch(RpcDecodedMessage msg, out Task<object?> task)
    {
        task = Task.FromResult<object?>(null);
        return false;
    }
}
