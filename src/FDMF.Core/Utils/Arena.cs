using System.Runtime.CompilerServices;

#pragma warning disable CS9092 // This returns a member of local by reference but it is not a ref local

namespace FDMF.Core.Utils;


public struct RelativePtr<T> where T : unmanaged
{
    public nint Offset;

    public unsafe RelativePtr(void* @base, T* ptr)
    {
        Offset = (IntPtr)((byte*)ptr - (byte*)@base);
    }

    public unsafe T* ToAbsolute(ArenaScope arenaScope)
    {
        return (T*)(arenaScope.Arena.BasePtr + arenaScope.Pos + Offset);
    }
}

public struct ArenaScope : IDisposable
{
    public required Arena Arena;
    public required int Pos;

    // public unsafe RelativePtr<T> GetRelativePtr<T>(T* ptr) where T : unmanaged
    // {
    //     return new RelativePtr<T>
    //     {
    //         Offset = (nint)ptr - ((nint)Arena.BasePtr + (nint)Pos)
    //     };
    // }

    public void Dispose()
    {
        Arena.PopTo(Pos);
    }

    public unsafe Slice<byte> GetAsSlice()
    {
        var start = Arena.BasePtr + Pos;
        return new Slice<byte>(start, (int)((nint)Arena.BasePtr + (nint)Arena.Pos - (nint)start));
    }
}

public sealed unsafe class Arena : IDisposable
{
    public byte* BasePtr;
    public int Pos;
    private int Size;
    private int Committed;

    public Arena(int capacityInBytes)
    {
        capacityInBytes = AlignToUpper(capacityInBytes, System.Environment.SystemPageSize);
        BasePtr = Platform.Reserve((nuint)capacityInBytes);

        if (BasePtr == null)
            throw new OutOfMemoryException();

        Pos = 0;
        Size = capacityInBytes;
        Committed = 0;
    }

    public Slice<byte> GetRegion(byte* start)
    {
        return new Slice<byte>(start, (int)(BasePtr + Pos - (nint)start));
    }

    public void Reset()
    {
        if (Committed > 0)
        {
            Platform.Decommit(BasePtr, (nuint)Committed);
            Committed = 0;
        }

        Pos = 0;
    }

    public ArenaScope Scope()
    {
        return new ArenaScope
        {
            Arena = this,
            Pos = Pos,
        };
    }

    public Slice<byte> AllocateSlice(ReadOnlySpan<byte> data)
    {
        var slice = AllocateSlice<byte>(data.Length);
        data.CopyTo(slice.Span);

        return slice;
    }

    public void* Push(int size)
    {
        var posAligned = AlignUpPow2(Pos, sizeof(void*));
        var newPos = posAligned + size;

        EnsureCapacity(newPos);

        Pos = newPos;

        byte* @out = BasePtr + posAligned;

        Unsafe.InitBlock(@out, 0, (uint)size);

        return @out;
    }

    public void PopTo(int pos)
    {
        Pos = pos;
    }

    public T* Allocate<T>(T value) where T : unmanaged
    {
        var v = (T*) Push(sizeof(T));
        *v = value;
        return v;
    }

    public Slice<T> AllocateSlice<T>(int count) where T : unmanaged
    {
        if (count == 0)
            return new Slice<T>(null, 0);

        var ptr = (T*)Push(sizeof(T) * count);
        return new Slice<T>
        {
            Items = ptr,
            Length = count
        };
    }

    private void EnsureCapacity(int requestedOffset)
    {
        if (requestedOffset <= Committed)
            return;

        int pageSize = System.Environment.SystemPageSize;

        int newCommitSize = AlignToUpper(requestedOffset - Committed, pageSize);

        if (Committed + newCommitSize > Size)
            throw new OutOfMemoryException("Arena exceeded reserved size");

        Platform.Commit(BasePtr + Committed, (nuint)newCommitSize);

        Committed += newCommitSize;
    }

    public void Dispose()
    {
        Platform.Release(BasePtr, (nuint)Size);
    }

    private static int AlignToUpper(int value, int align)
    {
        var nextValue = ((value + align - 1) / align) * align;
        return nextValue;
    }

    private int AlignUpPow2(int n, int p)
    {
        return (n + (p - 1)) & ~(p - 1);
    }

}