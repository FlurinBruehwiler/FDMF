using System.Runtime.InteropServices;

namespace FDMF.Core.PlatformLayer;

public sealed unsafe class LinuxPlatform : IPlatform
{
    public byte* Reserve(nuint bytes)
    {
        // On POSIX, mmap reserves address space; pages are committed on first touch.
        var result = mmap(IntPtr.Zero, bytes, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
        if (result == (IntPtr)(-1))
            throw new OutOfMemoryException();
        return (byte*)result;
    }

    public void Commit(byte* address, nuint bytes)
    {
        // No-op: memory is committed on demand by the OS.
    }

    public void Decommit(byte* address, nuint bytes)
    {
        if (bytes == 0)
            return;

        // Hint to the OS that the pages can be reclaimed.
        // MADV_DONTNEED will discard pages; they read as zero on next access.
        madvise((IntPtr)address, bytes, MADV_DONTNEED);
    }

    public void Release(byte* address, nuint bytes)
    {
        if (bytes == 0)
            return;

        munmap((IntPtr)address, bytes);
    }

    private const int PROT_READ = 0x1;
    private const int PROT_WRITE = 0x2;

    private const int MAP_PRIVATE = 0x02;
    private const int MAP_ANONYMOUS = 0x20;

    private const int MADV_DONTNEED = 4;

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr mmap(IntPtr addr, nuint length, int prot, int flags, int fd, nint offset);

    [DllImport("libc", SetLastError = true)]
    private static extern int munmap(IntPtr addr, nuint length);

    [DllImport("libc", SetLastError = true)]
    private static extern int madvise(IntPtr addr, nuint length, int advice);
}
