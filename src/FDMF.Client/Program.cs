using FDMF.Client;
using FDMF.Core;
using FDMF.Core.Database;
using Environment = FDMF.Core.Environment;

//todo, the goal is that the server and client can be in the same process!!!
//we still want to serialize / deserialize everything, so we get the exact same behaviour
//but we don't want a direct dependency on the WebSocket in the ServerProcedures

using var env = Environment.CreateDatabase("clientDb");

Logging.LogFlags = LogFlags.Error;

var clientProcedures = new ClientProcedures();

var state = Connection.CreateClientState();

Helper.FireAndForget(Connection.ConnectRemote(clientProcedures, state));

while (true)
{
    var res = await state.ServerProcedures.GetStatus(1, 2);
    Logging.Log(LogFlags.Info, res.ToString());
}