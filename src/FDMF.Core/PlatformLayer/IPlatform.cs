namespace FDMF.Core.PlatformLayer;

public interface IPlatform
{
    unsafe byte* Reserve(nuint bytes);
    unsafe void Commit(byte* address, nuint bytes);
    unsafe void Decommit(byte* address, nuint bytes);
    unsafe void Release(byte* address, nuint bytes);
}
