using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LightningDB;
using Shared.Utils;

namespace Shared.Database;

public static class Extensions
{
    public static unsafe Slice<byte> AsSlice(this MDBValue value)
    {
        var span = value.AsSpan();
        ref readonly byte r = ref MemoryMarshal.GetReference(span);
        byte* ptr = (byte*)Unsafe.AsPointer(ref Unsafe.AsRef(in r));
        return new Slice<byte>(ptr, span.Length);
    }

    public static Span<byte> AsSpan<T>(this ref T value) where T : unmanaged
    {
        return MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1));
    }

    public static void CopyToReverse<T>(this ReadOnlySpan<T> source, Span<T> destination)
    {
        if (destination.Length < source.Length)
            throw new ArgumentException("Destination too small");

        for (int i = 0, j = source.Length - 1; i < source.Length; i++, j--)
        {
            destination[i] = source[j];
        }
    }
}