using System.Diagnostics;
using System.Runtime.CompilerServices;
using TheProject;
using TheProject.Generated;
using Environment = TheProject.Environment;

//we can store all fields objId+fieldIds that where changed in a dictionary within the transaction,
//when saving, we have a separate table where we store the "history" of all objects
//we could directly add the entries to hist db in a new transaction.
//what we want is to group often used objects together for better cache efficiency, and so that these pages can be unloaded from memory



var env = Environment.Create();

using var transaction = new Transaction(env);

var folder = new Folder(transaction);
folder.Name = "child :(";

var parentFolder = new Folder(transaction);
parentFolder.Name = "parent :)";

folder.Parent = parentFolder;

Console.WriteLine("These are the children:");
foreach (var subfolders in parentFolder.Subfolders)
{
    Console.WriteLine(subfolders.Name);
}

folder.Parent = null;

if (folder.Parent == null)
{
    Console.WriteLine("Has no more parent");
}

parentFolder.Subfolders.Add(folder);
parentFolder.Subfolders.Remove(folder);
parentFolder.Subfolders.Add(folder);
parentFolder.Subfolders.Clear();

Debug.Assert(parentFolder.Subfolders.Count == 0);

parentFolder.Subfolders.Add(folder);
parentFolder.Subfolders.Add(folder);

Debug.Assert(parentFolder.Subfolders.Count == 1);

transaction.Commit();



