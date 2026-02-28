using FDMF.Client;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

//todo, the goal is that the server and client can be in the same process!!!
//we still want to serialize / deserialize everything, so we get the exact same behaviour
//but we don't want a direct dependency on the WebSocket in the ServerProcedures

using var env = DbEnvironment.CreateDatabase("clientDb");

Logging.LogFlags = LogFlags.Error;

var clientProcedures = new ClientProcedures();

Dictionary<Guid, PendingRequest> callbacks = [];

Helper.FireAndForget(Connection.ConnectRemote(clientProcedures, callbacks));

