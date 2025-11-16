using Client;
using Model;

//todo, the goal is that the server and client can be in the same process!!!
//we still want to serialize / deserialize everything, so we get the exact same behaviour
//but we don't want a direct dependency on the WebSocket in the ServerProcedures

Logging.LogFlags = LogFlags.All;

var clientProcedures = new ClientProceduresImpl();

var state = Connection.CreateClientState();

Helper.FireAndForget(Connection.ConnectRemote(clientProcedures, state));

while (true)
{
    var res = await state.ServerProcedures.GetStatus(1, 2);
    Thread.Sleep(1000);
    Logging.Log(LogFlags.Info, res.ToString());
}