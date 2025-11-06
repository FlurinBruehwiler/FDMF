using LightningDB;
using TheProject;
using TheProject.Generated;

var env = new LightningEnvironment("path.db");
env.Open();

using var transaction = new Transaction(env);

var folder = new Folder(transaction);
folder.Name = "child :(";

var parentFolder = new Folder(transaction);
parentFolder.Name = "parent :)";

folder.Parent = parentFolder; //todo what if the assoc is non nullable?

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

// transaction.Commit();



