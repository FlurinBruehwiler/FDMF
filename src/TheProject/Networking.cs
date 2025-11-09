using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TheProject;

/*
 * Goals of this networking implementation:
 * RPC: A procedure can have n parameters and a return value.
 *      The parameter and return value are individually binary serialized/deserialized
 * Support large values, therefor we need some kind of message interleaving
 */

public class NetworkingClient
{
    public WebSocket WebSocket;
    public Dictionary<Guid, TaskCompletionSource<Memory<byte>>> Callbacks = [];

    //do we want to make this stream based, so that we can better handle large files?
    public Task<Memory<byte>> SendMessage(Memory<byte> input)
    {
        var messageIdentifier = Guid.NewGuid();
        WebSocket.SendAsync(input, WebSocketMessageType.Binary, true, CancellationToken.None); //todo what if the task fails?

        TaskCompletionSource<Memory<byte>> tsc = new TaskCompletionSource<Memory<byte>>(); //can we avoid this allocation?
        Callbacks.Add(messageIdentifier, tsc);

        return tsc.Task;
    }

    public async Task ListenForMessages()
    {
        var buffer = new byte[4096];
        while (WebSocket.State == WebSocketState.Open)
        {
            await ListenForMessage(buffer);
        }
    }

    private async Task ListenForMessage(byte[] buffer)
    {
        var messageBuffer = new List<byte>();

        WebSocketReceiveResult result;
        do
        {
            result = await WebSocket.ReceiveAsync(buffer, CancellationToken.None);
            messageBuffer.AddRange(buffer.AsSpan(result.Count));
        }
        while (!result.EndOfMessage);

        Memory<byte> arr = UnsafeAccessors<byte>.GetBackingArray(messageBuffer).AsMemory(0, messageBuffer.Count);
        var messageId = MemoryMarshal.Read<Guid>(arr.Span);
        if (Callbacks.TryGetValue(messageId, out var completionSource))
        {
            completionSource.SetResult(arr);
        }
        else
        {
            Console.WriteLine("Womp womp, this message was never send?");
        }
    }
}

public static class UnsafeAccessors<T>
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_items")]
    public static extern ref T[] GetBackingArray(List<T> list);
}

public class Networking
{
    public async Task ListenForClients()
    {
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost/connect");
        listener.Start();

        while (true)
        {
            var context = await listener.GetContextAsync();
            if (context.Request.IsWebSocketRequest)
            {
                var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);

                //client connected
                var mem = new byte[10];

            }
            else
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
            }
        }
    }
}