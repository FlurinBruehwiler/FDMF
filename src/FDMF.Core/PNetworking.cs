using System.Net.WebSockets;

namespace FDMF.Core;

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
    ConnectionClosed = 0,
    Request = 1,
    Response = 2,
    Notification = 3,
    Hello = 4,
}

public sealed class PNetworking
{
    public static ValueTask SendMessage(WebSocket webSocket, Memory<byte> input)
    {
         return webSocket.SendAsync(input, WebSocketMessageType.Binary, true, CancellationToken.None);
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
            Logging.Log(LogFlags.Info, "Waiting for a message");

            try
            {
                result = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
            }
            catch (Exception e)
            {
                Logging.Log(LogFlags.Info, $"Connection Closed: {e.Message}");
                return ReadOnlyMemory<byte>.Empty;
            }

            messageBuffer.AddRange(buffer.AsSpan(0, result.Count));
        }
        while (!result.EndOfMessage);

        Logging.LogMetric("Processed Messages");

        return messageBuffer.ToArray().AsMemory();
    }
}
