using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace FDMF.Core;

public ref struct BinaryReader
{
    public int CurrentOffset;
    public ReadOnlySpan<byte> Data;
    public bool HasError;

    public Guid ReadGuid()
    {
        if (!HasEnoughBytesRemaining(16))
        {
            CurrentOffset += 16;
            HasError = true;
            return Guid.Empty;
        }

        var s = Data.Slice(CurrentOffset, 16);
        var guid = MemoryMarshal.Read<Guid>(s);
        CurrentOffset += 16;
        return guid;
    }

    public byte ReadByte()
    {
        if (!HasEnoughBytesRemaining(1))
        {
            CurrentOffset++;

            HasError = true;
            return 0;
        }

        var b = Data[CurrentOffset];
        CurrentOffset++;
        return b;
    }

    public int ReadInt32()
    {
        if (BinaryPrimitives.TryReadInt32LittleEndian(Data.Slice(CurrentOffset), out var i))
        {
            CurrentOffset += 4;

            return i;
        }

        CurrentOffset += 4;
        HasError = true;
        return 0;
    }

    public ReadOnlySpan<byte> ReadSlice(int requestedLength)
    {
        if (!HasEnoughBytesRemaining(requestedLength))
        {
            HasError = true;
            CurrentOffset += requestedLength;
            return ReadOnlySpan<byte>.Empty;
        }

        var data = Data.Slice(CurrentOffset, requestedLength);

        CurrentOffset += requestedLength;

        return data;
    }

    public ReadOnlySpan<char> ReadUtf16String()
    {
        var lengthOfStringInBytes = ReadInt32();
        return MemoryMarshal.Cast<byte, char>(ReadSlice(lengthOfStringInBytes));
    }

    private bool HasEnoughBytesRemaining(int byteCount)
    {
        return Data.Length - CurrentOffset >= byteCount;
    }
}