using System.Reflection;
using MemoryPack;

namespace FDMF.Core.Rpc;

public sealed class RpcEndpoint
{
    private readonly IRpcFrameTransport _transport;
    private readonly object _handler;
    private readonly int _protocolVersion;

    private readonly TaskCompletionSource _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _connected;

    private readonly Dictionary<Guid, PendingRequest> _pending = new();
    private readonly Dictionary<string, MethodInfo> _methodCache = new(StringComparer.Ordinal);

    public RpcEndpoint(IRpcFrameTransport transport, object handler, int protocolVersion = RpcProtocol.Version)
    {
        _transport = transport;
        _handler = handler;
        _protocolVersion = protocolVersion;
    }

    public Task Connected => _connectedTcs.Task;

    public Guid SendRequest(string methodName, object[] parameters, bool isNotification)
    {
        var requestId = Guid.NewGuid();
        var msgType = isNotification ? MessageType.Notification : MessageType.Request;
        var frame = RpcCodec.EncodeRequest(msgType, requestId, methodName, parameters);

        _ = SendFrameWhenConnected(frame);
        return requestId;
    }

    private async Task SendFrameWhenConnected(byte[] frame)
    {
        await Connected.ConfigureAwait(false);
        await _transport.SendFrameAsync(frame).ConfigureAwait(false);
    }

    public Task<T> WaitForResponse<T>(Guid requestId)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pending.Add(requestId, new PendingRequest
        {
            ResponseType = typeof(T),
            Callback = resp => tcs.TrySetResult((T)resp)
        });

        return tcs.Task;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Initiate handshake.
        try
        {
            await _transport.SendFrameAsync(RpcCodec.EncodeHello(_protocolVersion), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _connectedTcs.TrySetException(e);
            throw;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await _transport.ReceiveFrameAsync(cancellationToken);
            if (frame.IsEmpty)
                return;

            if (!RpcCodec.TryDecode(frame, out var msg))
                continue;

            if (!_connected)
            {
                if (msg.Type != MessageType.Hello)
                    throw new InvalidOperationException($"Expected RPC hello, got {msg.Type}");

                if (!msg.HelloVersion.HasValue)
                    throw new InvalidOperationException("Malformed RPC hello");

                if (msg.HelloVersion.Value != _protocolVersion)
                {
                    var ex = new InvalidOperationException($"RPC protocol version mismatch. Local={_protocolVersion}, Remote={msg.HelloVersion.Value}");
                    _connectedTcs.TrySetException(ex);
                    throw ex;
                }

                _connected = true;
                _connectedTcs.TrySetResult();
                continue;
            }

            switch (msg.Type)
            {
                case MessageType.Hello:
                    // Ignore redundant hellos.
                    break;
                case MessageType.Request:
                case MessageType.Notification:
                    await HandleIncomingRequest(msg, cancellationToken);
                    break;

                case MessageType.Response:
                    HandleIncomingResponse(msg);
                    break;

                case MessageType.ConnectionClosed:
                    return;
            }
        }
    }

    private void HandleIncomingResponse(RpcDecodedMessage msg)
    {
        if (_pending.Remove(msg.RequestId, out var pending))
        {
            var obj = MemoryPackSerializer.Deserialize(pending.ResponseType, msg.Payload.Span, RpcCodec.SerializerOptions);
            pending.Callback(obj!);
        }
        else
        {
            Logging.Log(LogFlags.Error, $"No pending request for id {msg.RequestId}");
        }
    }

    private async Task HandleIncomingRequest(RpcDecodedMessage msg, CancellationToken cancellationToken)
    {
        if (msg.MethodName is null)
            return;

        if (!_methodCache.TryGetValue(msg.MethodName, out var method))
        {
            method = _handler.GetType().GetMethod(msg.MethodName, BindingFlags.Public | BindingFlags.Instance)!;
            if (method == null)
            {
                Logging.Log(LogFlags.Error, $"Could not find procedure '{msg.MethodName}'");
                return;
            }
            _methodCache[msg.MethodName] = method;
        }

        var parametersInfo = method.GetParameters();
        var argPayloads = msg.ArgPayloads ?? [];
        if (parametersInfo.Length != argPayloads.Length)
        {
            Logging.Log(LogFlags.Error, $"Arg count mismatch for '{msg.MethodName}', got {argPayloads.Length}, expected {parametersInfo.Length}");
            return;
        }

        var args = new object?[argPayloads.Length];
        for (int i = 0; i < argPayloads.Length; i++)
        {
            var paramType = parametersInfo[i].ParameterType;
            args[i] = MemoryPackSerializer.Deserialize(paramType, argPayloads[i].Span, RpcCodec.SerializerOptions);
        }

        object? returnObject;
        try
        {
            returnObject = method.Invoke(_handler, args);
        }
        catch (TargetInvocationException tie)
        {
            Logging.LogException(tie.InnerException ?? tie);
            return;
        }

        if (msg.Type == MessageType.Notification)
            return;

        if (returnObject is not Task task)
        {
            Logging.Log(LogFlags.Error, $"Invalid return type for '{msg.MethodName}', expected Task");
            return;
        }

        await task.ConfigureAwait(false);

        object? result = null;
        var taskType = task.GetType();
        if (taskType.IsGenericType)
        {
            // Task<T>
            result = taskType.GetProperty("Result")?.GetValue(task);
        }

        if (result == null)
        {
            // For now: respond with an empty ServerStatus-like default isn't possible.
            // We assume RPC methods are Task<T>.
            Logging.Log(LogFlags.Error, $"Response for '{msg.MethodName}' was null");
            return;
        }

        var responseFrame = RpcCodec.EncodeResponse(msg.RequestId, result);
        await _transport.SendFrameAsync(responseFrame, cancellationToken);
    }
}
