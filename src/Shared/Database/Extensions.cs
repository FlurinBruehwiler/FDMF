namespace Shared.Database;

public static class Extensions
{
    public static Slice<T> AsSlice<T>(this ReadOnlySpan<T> span) where T : unmanaged
    {
        return new Slice<T>(span);
    }
}