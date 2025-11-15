using Model;
using Server;

//we can store all fields objId+fieldIds that where changed in a dictionary within the transaction,
//when saving, we have a separate table where we store the "history" of all objects
//we could directly add the entries to hist db in a new transaction.
//what we want is to group often used objects together for better cache efficiency, and so that these pages can be unloaded from memory

Logging.LogFlags = LogFlags.Error | LogFlags.Performance;

var sm = new ServerManager();

Helper.FireAndForget(sm.LogMetrics());

await sm.ListenForConnections();

