using LightningDB;

namespace Shared;

public class Environment
{
    public required LightningEnvironment LightningEnvironment;
    public required LightningDatabase ObjectDb;
    public required LightningDatabase HistoryDb;
    public required LightningDatabase SearchIndex;
    public required HashSet<Guid> FldsToIndex;

    public static Environment Create(HashSet<Guid> fldsToIndex)
    {
        //during testing we delete the old db
        Directory.Delete("database", recursive: true);

        var env = new LightningEnvironment("database", new EnvironmentConfiguration
        {
            MaxDatabases = 128
        });
        env.Open();

        using var lightningTransaction = env.BeginTransaction();

        var objDb = lightningTransaction.OpenDatabase(null, new DatabaseConfiguration
        {
            Flags = DatabaseOpenFlags.Create
        });

        var histDb = lightningTransaction.OpenDatabase(name: "HistoryDb", new DatabaseConfiguration
        {
            Flags = DatabaseOpenFlags.Create
        });

        var indexDb = lightningTransaction.OpenDatabase(name: "IndexDb", new DatabaseConfiguration
        {
            Flags = DatabaseOpenFlags.Create | DatabaseOpenFlags.DuplicatesSort
        });

        lightningTransaction.Commit();

        return new Environment
        {
            LightningEnvironment = env,
            ObjectDb = objDb,
            HistoryDb = histDb,
            SearchIndex = indexDb,
            FldsToIndex = fldsToIndex
        };
    }
}