using System.Runtime.InteropServices;

namespace Shared.PlatformLayer;

public sealed unsafe class WindowsPlatform : IPlatform
{
    public byte* Reserve(nuint bytes)
    {
        var ptr = VirtualAlloc(IntPtr.Zero, bytes, AllocationType.Reserve, MemoryProtection.ReadWrite);
        if (ptr == IntPtr.Zero)
            throw new OutOfMemoryException();
        return (byte*)ptr;
    }

    public void Commit(byte* address, nuint bytes)
    {
        var result = VirtualAlloc((IntPtr)address, bytes, AllocationType.Commit, MemoryProtection.ReadWrite);
        if (result == IntPtr.Zero)
            throw new OutOfMemoryException();
    }

    public void Decommit(byte* address, nuint bytes)
    {
        if (bytes == 0)
            return;
        var res = VirtualFree((IntPtr)address, bytes, FreeType.Decommit);
        if (res == 0)
            throw new InvalidOperationException($"VirtualFree(MEM_DECOMMIT) failed: {Marshal.GetLastWin32Error()}");
    }

    public void Release(byte* address, nuint bytes)
    {
        // MEM_RELEASE requires dwSize == 0.
        var res = VirtualFree((IntPtr)address, 0, FreeType.Release);
        if (res == 0)
            throw new InvalidOperationException($"VirtualFree(MEM_RELEASE) failed: {Marshal.GetLastWin32Error()}");
    }

    [DllImport("kernel32", EntryPoint = nameof(VirtualAlloc), SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddress, nuint dwSize, AllocationType flAllocationType, MemoryProtection flProtect);

    [DllImport("kernel32", EntryPoint = nameof(VirtualFree), SetLastError = true)]
    private static extern int VirtualFree(IntPtr lpAddress, nuint dwSize, FreeType dwFreeType);

    [Flags]
    private enum AllocationType
    {
        Commit = 0x1000,
        Reserve = 0x2000,
    }

    [Flags]
    private enum MemoryProtection
    {
        ReadWrite = 0x04,
    }

    [Flags]
    private enum FreeType
    {
        Decommit = 0x4000,
        Release = 0x8000,
    }
}
