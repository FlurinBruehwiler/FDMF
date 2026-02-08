using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FDMF.Core.DatabaseLayer;

public static class Helper
{
    public static string GetRootDir([CallerFilePath] string str = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(str)!, "../"));
    }

    public static bool MemoryEquals<T>(T val, T other) where T : unmanaged
    {
        ReadOnlySpan<byte> value = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref val, 1));

        ReadOnlySpan<byte> otherValue = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref other, 1));

        return value.SequenceEqual(otherValue);
    }

    public static void FireAndForget(Task t)
    {
        t.ContinueWith(x =>
        {
            Logging.LogException(x.Exception);
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}