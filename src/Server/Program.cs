using Model.Generated;
using Shared;
using Shared.Database;

//we can store all fields objId+fieldIds that where changed in a dictionary within the transaction,
//when saving, we have a separate table where we store the "history" of all objects
//we could directly add the entries to hist db in a new transaction.
//what we want is to group often used objects together for better cache efficiency, and so that these pages can be unloaded from memory

try
{
    var root = Helper.GetRootDir();
    var model = ProjectModel.CreateFromDirectory(Path.Combine(root, "Shared/Model"));

    using var env = Shared.Environment.Create(model);

    Guid parentFolderObjId;
    Guid childObjId;
    using (var tsx = new DbSession(env))
    {
        var parentFolder = new Folder(tsx)
        {
            Name = "Parent",
            TestDateField = new DateTime(2004, 09, 13),
        };
        parentFolderObjId = parentFolder.ObjId;

        var childFolder = new Folder(tsx)
        {
            Name = "Child",
            TestDateField = new DateTime(2004, 09, 13),
            Parent = parentFolder
        };
        childObjId = childFolder.ObjId;

        new Folder(tsx)
        {
            Name = "Flurin Flu"
        };

        new Folder(tsx)
        {
            Name = "Anna max Wynn"
        };

        new Folder(tsx)
        {
            Name = "Flurin"
        };

        tsx.Commit();

        Console.WriteLine(JsonDump.GetJsonDump(env, tsx));
    }
}
catch (Exception e)
{
    Console.WriteLine(e);
    Console.WriteLine(e.StackTrace);
}