namespace Shared.Database;

public unsafe struct Slice<T> where T : unmanaged
{
    public Slice(ReadOnlySpan<T> span)
    {
        fixed (T* ptr = span) //wow, this is really unsafe!!!!
        {
            Items = ptr;
            Length = span.Length;
        }
    }

    public Slice(T* items, int length)
    {
        Items = items;
        Length = length;
    }

    public T* Items;
    public int Length;

    public Span<T> AsSpan()
    {
        return new Span<T>(Items, Length);
    }

    public Slice<byte> AsByteSlice()
    {
        return new Slice<byte>((byte*)Items, Length * sizeof(T));
    }

    public static Slice<T> Empty()
    {
        return new Slice<T>();
    }
}