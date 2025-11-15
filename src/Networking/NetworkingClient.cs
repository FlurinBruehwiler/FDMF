using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;

namespace Networking;

public struct PendingRequest
{
    public Action<object> Callback;
    public Type ResponseType;
}

public static class NetworkingClient
{
    public static Task<T> WaitForResponse<T>(Dictionary<Guid, PendingRequest> callbacks, Guid guid)
    {
        var tsc = new TaskCompletionSource<T>();

        callbacks.Add(guid, new PendingRequest
        {
            ResponseType = typeof(T),
            Callback = response =>
            {
                tsc.SetResult((T)response);
            }
        });

        return tsc.Task;
    }

    public static Guid SendRequest(Action<Stream> sendMessage, string methodName, object[] parameters, bool isNotification)
    {
        var requestGuid = Guid.NewGuid();

        //todo avoid this memory alloc

        using var memStream = new MemoryStream();
        using var writer = new BinaryWriter(memStream, Encoding.Unicode, true);
        writer.Write((byte)(isNotification ? MessageType.Notification : MessageType.Request));
        writer.Write(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref requestGuid, 1)));
        writer.Write(methodName);
        writer.Write(parameters.Length);

        foreach (var parameter in parameters)
        {
            var data = MemoryPackSerializer.Serialize(parameter.GetType(), parameter);
            writer.Write(data.Length);
            writer.Write(data);
        }

        sendMessage(memStream);

        return requestGuid;
    }

    public static async Task ProcessMessagesForWebSocket(WebSocket webSocket, object messageHandler, Dictionary<Guid, PendingRequest> callbacks)
    {
        while (webSocket.State == WebSocketState.Open)
        {
            var messageContent = (await PNetworking.GetNextMessage(webSocket)).Span;

            var binaryReader = new BinaryReader
            {
                Data = messageContent,
            };

            var messageType = (MessageType)binaryReader.ReadByte();

            if (messageType == MessageType.Request || messageType == MessageType.Notification)
            {
                var requestId = binaryReader.ReadGuid();
                var procedureName = binaryReader.ReadUtf16String();
                var argCount = binaryReader.ReadByte();

                //this is just prototype code, in the future we want to do this without reflection...
                var method = messageHandler.GetType().GetMethod(procedureName.ToString(), BindingFlags.Public | BindingFlags.Instance);
                if (method != null)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == argCount)
                    {
                        var parameterObjects = new object?[argCount];

                        for (int i = 0; i < argCount; i++)
                        {
                            var offset = binaryReader.ReadInt32();
                            var length = binaryReader.ReadInt32();
                            var parameterData = messageContent.Slice(offset, length);

                            var paramType = parameters[i];
                            var paraObj = MemoryPackSerializer.Deserialize(paramType.ParameterType, parameterData, MemoryPackSerializerOptions.Default);
                            parameterObjects[i] = paraObj;
                        }

                        var returnValue = method.Invoke(messageHandler, parameterObjects);

                        //if it is a notification, we don't send a response back
                        if (messageType == MessageType.Request)
                        {
                            var res = MemoryPackSerializer.Serialize(returnValue, MemoryPackSerializerOptions.Default);
                            byte[] response = new byte[res.Length + 1 + 4];
                            response[0] = (byte)MessageType.Response;

                            MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref requestId, 1)).CopyTo(response.AsSpan(1));
                            res.AsSpan().CopyTo(response.AsSpan(5));

                            PNetworking.SendMessage(webSocket, response);
                        }
                    }
                }
            }
            else if (messageType == MessageType.Response)
            {
                var requestId = binaryReader.ReadGuid();
                var data = binaryReader.Data.Slice(binaryReader.CurrentOffset);

                if (callbacks.Remove(requestId, out var pendingRequest))
                {
                    var obj = MemoryPackSerializer.Deserialize(pendingRequest.ResponseType, data);
                    pendingRequest.Callback(obj);
                }
                else
                {
                    Console.WriteLine($"No request for id {requestId}");
                }
            }
            else
            {
                Console.WriteLine($"Invalid message type {messageType}");
            }
        }
    }
}