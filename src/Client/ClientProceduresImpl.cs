using Model;
using Networking;

namespace Client;

class ClientProceduresImpl : IClientProcedures
{
    public void Ping()
    {
        Logging.Log(LogFlags.Info, "Got Ping");
    }
}