using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.WebSockets;

namespace FDMF.Core.Rpc;

public interface IRpcFrameTransport : IAsyncDisposable
{
    ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default);
    ValueTask<ReadOnlyMemory<byte>> ReceiveFrameAsync(CancellationToken cancellationToken = default);
}

public sealed class WebSocketFrameTransport(WebSocket webSocket) : IRpcFrameTransport
{
    public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        if (webSocket.State != WebSocketState.Open)
            return;

        await webSocket.SendAsync(frame, WebSocketMessageType.Binary, true, cancellationToken);
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReceiveFrameAsync(CancellationToken cancellationToken = default)
    {
        return await PNetworking.GetNextMessage(webSocket);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// Length-prefixed framing for stream transports like named pipes.
// Frame format: [int32 little-endian length][frame bytes]
public sealed class NamedPipeFrameTransport(Stream stream) : IRpcFrameTransport
{
    private readonly byte[] _lenBuf = new byte[4];

    public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_lenBuf, frame.Length);
        await stream.WriteAsync(_lenBuf, cancellationToken);
        await stream.WriteAsync(frame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReceiveFrameAsync(CancellationToken cancellationToken = default)
    {
        await ReadExactlyAsync(stream, _lenBuf, cancellationToken);
        var len = BinaryPrimitives.ReadInt32LittleEndian(_lenBuf);
        if (len < 0)
            throw new InvalidOperationException($"Invalid frame length {len}");

        var buf = new byte[len];
        await ReadExactlyAsync(stream, buf, cancellationToken);
        return buf;
    }

    private static async Task ReadExactlyAsync(Stream s, byte[] buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await s.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken);
            if (n == 0)
                throw new EndOfStreamException("Stream closed while reading frame");
            read += n;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
