using System.Buffers;
using System.Collections.Generic;

namespace Shared.Database;

/// <summary>
/// Simple arena allocator for byte slices. Allocations are freed in bulk via <see cref="Reset"/> or <see cref="Dispose"/>.
/// </summary>
public sealed class ByteArena : IDisposable
{
    private readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;
    private readonly List<byte[]> _blocks = [];

    private byte[]? _current;
    private int _currentOffset;

    public int BlockSize { get; }

    public ByteArena(int blockSize = 64 * 1024)
    {
        if (blockSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(blockSize));

        BlockSize = blockSize;
    }

    public ReadOnlyMemory<byte> Copy(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return ReadOnlyMemory<byte>.Empty;

        var writable = Allocate(data.Length, out var slice);
        data.CopyTo(writable.Span);
        return slice;
    }

    public Memory<byte> Allocate(int length, out ReadOnlyMemory<byte> slice)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        if (length == 0)
        {
            slice = ReadOnlyMemory<byte>.Empty;
            return Memory<byte>.Empty;
        }

        EnsureCapacity(length);

        var start = _currentOffset;
        _currentOffset += length;

        var mem = _current!.AsMemory(start, length);
        slice = mem;
        return mem;
    }

    public void Reset()
    {
        if (_blocks.Count == 0)
        {
            _current = null;
            _currentOffset = 0;
            return;
        }

        _current = _blocks[0];
        _currentOffset = 0;

        for (int i = 1; i < _blocks.Count; i++)
        {
            _pool.Return(_blocks[i]);
        }

        _blocks.RemoveRange(1, _blocks.Count - 1);
    }

    public void Dispose()
    {
        foreach (var block in _blocks)
        {
            _pool.Return(block);
        }

        _blocks.Clear();
        _current = null;
        _currentOffset = 0;
    }

    private void EnsureCapacity(int length)
    {
        if (_current == null)
        {
            RentNewBlock(Math.Max(BlockSize, length));
            return;
        }

        var remaining = _current.Length - _currentOffset;
        if (remaining >= length)
            return;

        RentNewBlock(Math.Max(BlockSize, length));
    }

    private void RentNewBlock(int minSize)
    {
        _current = _pool.Rent(minSize);
        _blocks.Add(_current);
        _currentOffset = 0;
    }
}
