using MemoryPack;

namespace Shared;

[MemoryPackable]
public partial struct ServerStatus
{

}

public interface IServerProcedures
{
    public Task<ServerStatus> GetStatus(int a, int b);
}