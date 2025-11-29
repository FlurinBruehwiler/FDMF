using Shared;

namespace Client;

class ClientProcedures : IClientProcedures
{
    public void Ping()
    {
        Logging.Log(LogFlags.Info, "Got Ping");
    }
}