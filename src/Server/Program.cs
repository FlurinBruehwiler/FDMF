using Shared;
using Shared.Database;
using Shared.Generated;

//we can store all fields objId+fieldIds that where changed in a dictionary within the transaction,
//when saving, we have a separate table where we store the "history" of all objects
//we could directly add the entries to hist db in a new transaction.
//what we want is to group often used objects together for better cache efficiency, and so that these pages can be unloaded from memory

try
{
    var env = Shared.Environment.Create([Folder.Fields.Name]);

    Guid flurinFolder;

    using (var tsx = new DbSession(env))
    {
        new Folder(tsx)
        {
            Name = "Flurin"
        };

        flurinFolder = new Folder(tsx)
        {
            Name = "Flurin Brühwiler"
        }.ObjId;

        new Folder(tsx)
        {
            Name = "Firefox"
        };

        new Folder(tsx)
        {
            Name = "Anna"
        };

        tsx.Commit();
    }

    Searcher.BuildSearchIndex(env);

    using (var tsx = new DbSession(env))
    {
        new Folder(tsx)
        {
            Name = "Flurin 2"
        };

        var f = tsx.GetObjFromGuid<Folder>(flurinFolder);
        f.Name = "Johnny";

        tsx.Commit();
    }

    {
        using var t = new DbSession(env);

        foreach (var folder in Searcher.Search<Folder>(t, new FieldCriterion
                 {
                     FieldId = Folder.Fields.Name,
                     Value = "Flu"
                 }))
        {
            Console.WriteLine(folder.Name);
        }

        foreach (var folder in Searcher.Search<Folder>(t, new FieldCriterion
                 {
                     FieldId = Folder.Fields.Name,
                     Value = "nna"
                 }))
        {
            Console.WriteLine(folder.Name);
        }
    }
}
catch (Exception e)
{
    Console.WriteLine(e);
    Console.WriteLine(e.StackTrace);
}