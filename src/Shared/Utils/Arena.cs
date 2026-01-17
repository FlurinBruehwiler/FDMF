using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Shared.Database;
#pragma warning disable CS9092 // This returns a member of local by reference but it is not a ref local

namespace Shared.Utils;

public struct RelativePtr<T> where T : unmanaged
{
    public nint Offset;

    public unsafe RelativePtr(void* @base, T* ptr)
    {
        Offset = (byte)ptr - (byte)@base;
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
        BasePtr = (byte*)VirtualAlloc(IntPtr.Zero, (nuint)capacityInBytes, AllocationType.Reserve, MemoryProtection.ReadWrite);

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
            VirtualFree(
                (nint)BasePtr,
                (nuint)Committed,
                FreeType.Decommit
            );
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

    public unsafe T* Allocate<T>(T value) where T : unmanaged
    {
        var v = (T*) Push(sizeof(T));
        *v = value;
        return v;
    }

    public unsafe Slice<T> AllocateSlice<T>(int count) where T : unmanaged
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

        nint result = VirtualAlloc(
            (nint)(BasePtr + Committed),
            (nuint)newCommitSize,
            AllocationType.Commit,
            MemoryProtection.ReadWrite
        );

        if (result == 0)
            throw new OutOfMemoryException();

        Committed += newCommitSize;
    }

    public void Dispose()
    {
        VirtualFree((nint)BasePtr, (nuint)Size, FreeType.Release);
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

    //Interop -- Windows
    [DllImport("kernel32", EntryPoint = nameof(VirtualAlloc), SetLastError = true)]
    public static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, AllocationType flAllocationType, MemoryProtection flProtect);

    [DllImport("kernel32", EntryPoint = nameof(VirtualFree), SetLastError = true)]
    private static extern int VirtualFree(IntPtr lpAddress, nuint dwSize, FreeType dwFreeType);

    [DllImport("kernel32", EntryPoint = nameof(VirtualProtect), SetLastError = true)]
    private static extern int VirtualProtect(IntPtr lpAddress, nuint dwSize, MemoryProtection newProtect, out MemoryProtection oldProtect);

    [Flags]
    public enum AllocationType
    {
        Commit = 0x1000,
        Reserve = 0x2000,
        Decommit = 0x4000,
        Release = 0x8000,
        Reset = 0x80000,
        Physical = 0x400000,
        TopDown = 0x100000,
        WriteWatch = 0x200000,
        LargePages = 0x20000000
    }

    [Flags]
    public enum MemoryProtection
    {
        Execute = 0x10,
        ExecuteRead = 0x20,
        ExecuteReadWrite = 0x40,
        ExecuteWriteCopy = 0x80,
        NoAccess = 0x01,
        ReadOnly = 0x02,
        ReadWrite = 0x04,
        WriteCopy = 0x08,
        GuardModifierflag = 0x100,
        NoCacheModifierflag = 0x200,
        WriteCombineModifierflag = 0x400
    }

    [Flags]
    enum FreeType
    {
        Decommit = 0x4000,
        Release = 0x8000,
    }
}