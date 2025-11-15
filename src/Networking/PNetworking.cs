using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace Networking;

/*
 * Goals of this networking implementation:
 * RPC: A procedure can have n parameters and a return value.
 *      The parameter and return value are individually binary serialized/deserialized
 * Support large values, therefor we need some kind of message interleaving
 * Sending Objects to the client, and the server knowing that these objects where send, to that it can send updates once these objects change.
 * Do we want / need some kind of signals system that works across the network?
 * Do we always send the entire objects with all it's fields, or can we send single fields?
 */

//Messages Types: Request, Response, Notification

public enum MessageType : byte
{
    Request = 0,
    Response = 1,
    Notification = 2
}

public static class UnsafeAccessors<T>
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_items")]
    public static extern ref T[] GetBackingArray(List<T> list);
}

public class PNetworking
{
    public static void SendMessage(WebSocket webSocket, Memory<byte> input)
    {
         webSocket.SendAsync(input, WebSocketMessageType.Binary, true, CancellationToken.None);
    }

    public static async Task<ReadOnlyMemory<byte>> GetNextMessage(WebSocket webSocket)
    {
        if (webSocket.State != WebSocketState.Open)
            throw new Exception("This method should only be called as long as the websocket is open");

        //todo performance!!!!!!!!
        var buffer = new byte[4096];

        var messageBuffer = new List<byte>();

        WebSocketReceiveResult result;
        do
        {
            result = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
            messageBuffer.AddRange(buffer.AsSpan(result.Count));
        }
        while (!result.EndOfMessage);

        Memory<byte> arr = UnsafeAccessors<byte>.GetBackingArray(messageBuffer).AsMemory(0, messageBuffer.Count);

        return arr;
    }
}