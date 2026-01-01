using System.Buffers;
using System.Buffers.Binary;

namespace Shared.Database;

public ref struct PooledBufferWriter
{
    private byte[] _buffer;
    private int _position;
    private Span<byte> _guidScratch;

    public PooledBufferWriter(int initialCapacity, Span<byte> guidScratch)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        _position = 0;
        _guidScratch = guidScratch;
    }

    public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _position);

    public History.PooledLease DetachLease()
    {
        var lease = new History.PooledLease(_buffer, _position);
        _buffer = Array.Empty<byte>();
        _position = 0;
        _guidScratch = Span<byte>.Empty;
        return lease;
    }

    public void WriteByte(byte value)
    {
        Ensure(1);
        _buffer[_position++] = value;
    }

    public void WriteInt32(int value)
    {
        Ensure(4);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position, 4), value);
        _position += 4;
    }

    public void WriteInt64(long value)
    {
        Ensure(8);
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_position, 8), value);
        _position += 8;
    }

    public void WriteGuidLittleEndian(Guid guid)
    {
        guid.TryWriteBytes(_guidScratch, bigEndian: false, out _);
        WriteBytes(_guidScratch);
    }

    public int ReserveInt32()
    {
        Ensure(4);
        int offset = _position;
        _position += 4;
        return offset;
    }

    public void PatchInt32(int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(offset, 4), value);
    }

    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        Ensure(data.Length);
        data.CopyTo(_buffer.AsSpan(_position));
        _position += data.Length;
    }

    private void Ensure(int additional)
    {
        int required = _position + additional;
        if (required <= _buffer.Length)
            return;

        int newSize = Math.Max(required, _buffer.Length * 2);
        var newBuf = ArrayPool<byte>.Shared.Rent(newSize);
        _buffer.AsSpan(0, _position).CopyTo(newBuf);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuf;
    }
}