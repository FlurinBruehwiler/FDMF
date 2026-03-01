using System.Threading.Channels;

namespace FDMF.Core.Rpc;

public static class LocalFrameTransport
{
    public static (IRpcFrameTransport a, IRpcFrameTransport b) CreatePair()
    {
        var aToB = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
        var bToA = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

        return (
            new ChannelFrameTransport(inbound: bToA.Reader, outbound: aToB.Writer),
            new ChannelFrameTransport(inbound: aToB.Reader, outbound: bToA.Writer)
        );
    }

    private sealed class ChannelFrameTransport(ChannelReader<byte[]> inbound, ChannelWriter<byte[]> outbound) : IRpcFrameTransport
    {
        private bool _disposed;

        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ChannelFrameTransport));

            // Copy so callers can reuse their buffers safely.
            var copy = frame.ToArray();
            await outbound.WriteAsync(copy, cancellationToken);
        }

        public async ValueTask<ReadOnlyMemory<byte>> ReceiveFrameAsync(CancellationToken cancellationToken = default)
        {
            while (await inbound.WaitToReadAsync(cancellationToken))
            {
                if (inbound.TryRead(out var msg))
                    return msg;
            }

            // Closed.
            return ReadOnlyMemory<byte>.Empty;
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
                return ValueTask.CompletedTask;

            _disposed = true;
            outbound.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
