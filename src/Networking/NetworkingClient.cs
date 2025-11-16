using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using Model;

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
            Callback = response => { tsc.SetResult((T)response); }
        });

        return tsc.Task;
    }

    public static Guid SendRequest(Action<Stream> sendMessage, string methodName, object[] parameters, bool isNotification)
    {
        var requestGuid = Guid.NewGuid();

        Logging.Log(LogFlags.Info, $"Sending Request {methodName} ({requestGuid})");

        //todo avoid this memory alloc

        using var memStream = new MemoryStream();
        using var writer = new BinaryWriter(memStream, Encoding.Unicode, true);
        writer.Write((byte)(isNotification ? MessageType.Notification : MessageType.Request));

        Span<byte> s = stackalloc byte[16];
        MemoryMarshal.Write(s, requestGuid);
        writer.Write(s);

        writer.Write(methodName.Length * 2); //times 2 because of utf16
        writer.Write(methodName.AsSpan());

        writer.Write((byte)parameters.Length);

        foreach (var parameter in parameters)
        {
            var data = MemoryPackSerializer.Serialize(parameter.GetType(), parameter);
            writer.Write(data.Length);
            writer.Write(data);
        }

        sendMessage(memStream);

        return requestGuid;
    }

    public static async Task ProcessMessagesForWebSocket(WebSocket webSocket, SemaphoreSlim sendSemaphore, object messageHandler, Dictionary<Guid, PendingRequest> callbacks)
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

                Logging.Log(LogFlags.Info, $"Got Request with id {requestId}");

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
                            var length = binaryReader.ReadInt32();
                            var parameterData = binaryReader.ReadSlice(length);

                            var paramType = parameters[i];
                            var paraObj = MemoryPackSerializer.Deserialize(paramType.ParameterType, parameterData, MemoryPackSerializerOptions.Default);
                            parameterObjects[i] = paraObj;
                        }

                        Logging.Log(LogFlags.Info, $"Invoking method {procedureName}");
                        var returnObject = method.Invoke(messageHandler, parameterObjects);

                        //if it is a notification, we don't send a response back
                        if (messageType == MessageType.Request)
                        {
                            if (returnObject != null)
                            {
                                if (returnObject.GetType().IsAssignableTo(typeof(Task)))
                                {
                                    var returnTask = (Task)returnObject;
                                    returnTask.ContinueWith(async t =>
                                    {
                                        try
                                        {
                                            Logging.Log(LogFlags.Info, $"Sending response for id {requestId}");

                                            //really bad pls fix.....
                                            var r = t.GetType().GetProperty("Result").GetValue(returnObject);

                                            if (r != null)
                                            {
                                                var res = MemoryPackSerializer.Serialize(r.GetType(), r, MemoryPackSerializerOptions.Default);
                                                byte[] response = new byte[res.Length + 1 + 16];
                                                response[0] = (byte)MessageType.Response;

                                                Span<byte> x = stackalloc byte[16];
                                                MemoryMarshal.Write(x, requestId);
                                                x.CopyTo(response.AsSpan(1));

                                                res.AsSpan().CopyTo(response.AsSpan(17));

                                                //wow, this is code is so bad, please rewrite everything
                                                await sendSemaphore.WaitAsync();
                                                await PNetworking.SendMessage(webSocket, response);
                                                sendSemaphore.Release();
                                            }
                                            else
                                            {
                                                Logging.Log(LogFlags.Error, "Return type was null");
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            Logging.LogException(e);
                                        }
                                    });
                                }
                                else
                                {
                                    Logging.Log(LogFlags.Error, "Invalid return type, expected Task");
                                }
                            }
                            else
                            {
                                //todo
                                Logging.Log(LogFlags.Error, "Response was null");
                            }
                        }
                    }
                    else
                    {
                        Logging.Log(LogFlags.Error, $"Arg count for {procedureName} doesn't match, got {argCount}, expected {parameters.Length}");
                    }
                }
                else
                {
                    Logging.Log(LogFlags.Error, $"Could not find procedure with name {procedureName}");
                }
            }
            else if (messageType == MessageType.Response)
            {
                Logging.Log(LogFlags.Info, "Got response");

                var requestId = binaryReader.ReadGuid();
                var data = binaryReader.Data.Slice(binaryReader.CurrentOffset);

                if (callbacks.Remove(requestId, out var pendingRequest))
                {
                    var obj = MemoryPackSerializer.Deserialize(pendingRequest.ResponseType, data);
                    pendingRequest.Callback(obj);
                }
                else
                {
                    Logging.Log(LogFlags.Error, $"No request for id {requestId}");
                }
            }
            else
            {
                Logging.Log(LogFlags.Error, $"Invalid message type {messageType}");
            }
        }
    }
}