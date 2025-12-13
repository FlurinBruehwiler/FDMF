using Shared;
using Shared.Database;
using Shared.Generated;

//we can store all fields objId+fieldIds that where changed in a dictionary within the transaction,
//when saving, we have a separate table where we store the "history" of all objects
//we could directly add the entries to hist db in a new transaction.
//what we want is to group often used objects together for better cache efficiency, and so that these pages can be unloaded from memory

var env = Shared.Environment.Create();

var tsx = new DbSession(env);
{
    var folder = new Folder(tsx);
    folder.Name = "foo";
}
tsx.Commit();

var tsx2 = new DbSession(env);
{
    var folder = new Folder(tsx2);
    folder.Name = "foo2";
}
tsx2.Commit();

{
    var t = new DbSession(env);
    var folders = Searcher.Search<Folder>(t);
    foreach (var folder in folders)
    {
        Console.WriteLine(folder.Name);
    }
}

// var tsx2 = new Transaction(env);


// Logging.LogFlags = LogFlags.Error | LogFlags.Performance;
//
// var sm = new ServerManager();
//
// Helper.FireAndForget(sm.LogMetrics());
//
// await sm.ListenForConnections();

