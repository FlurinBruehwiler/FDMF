global using static FDMF.Core.PlatformLayer.Plt;

namespace FDMF.Core.PlatformLayer;

public static class Plt
{
    public static IPlatform Platform { get; } = Create();

    private static IPlatform Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsPlatform();

        // GitHub Actions ubuntu-latest and most servers
        if (OperatingSystem.IsLinux())
            return new LinuxPlatform();

        // Fallback for other OSes (macOS etc.)
        // LinuxPlatform uses libc calls that also exist on macOS, but constants differ,
        // so fail fast for now.
        throw new PlatformNotSupportedException("Unsupported OS for Arena platform layer.");
    }
}
