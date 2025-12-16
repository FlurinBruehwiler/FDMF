using System.Runtime.InteropServices;

namespace Shared.Database;

public static class Extensions
{
    public static Slice<T> AsSlice<T>(this ReadOnlySpan<T> span) where T : unmanaged
    {
        return new Slice<T>(span);
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