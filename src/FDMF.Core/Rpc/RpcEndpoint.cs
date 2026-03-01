using System.Reflection;
using MemoryPack;

namespace FDMF.Core.Rpc;

public sealed class RpcEndpoint
{
    private readonly IRpcFrameTransport _transport;
    private readonly object _handler;

    private readonly Dictionary<Guid, PendingRequest> _pending = new();
    private readonly Dictionary<string, MethodInfo> _methodCache = new(StringComparer.Ordinal);

    public RpcEndpoint(IRpcFrameTransport transport, object handler)
    {
        _transport = transport;
        _handler = handler;
    }

    public Guid SendRequest(string methodName, object[] parameters, bool isNotification)
    {
        var requestId = Guid.NewGuid();
        var msgType = isNotification ? MessageType.Notification : MessageType.Request;
        var frame = RpcCodec.EncodeRequest(msgType, requestId, methodName, parameters);
        _ = _transport.SendFrameAsync(frame);
        return requestId;
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
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await _transport.ReceiveFrameAsync(cancellationToken);
            if (frame.IsEmpty)
                return;

            if (!RpcCodec.TryDecode(frame, out var msg))
                continue;

            switch (msg.Type)
            {
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
