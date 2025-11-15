using LightningDB;

namespace Model;

public class Environment
{
    public required LightningEnvironment LightningEnvironment;
    public required LightningDatabase ObjectDb;
    public required LightningDatabase HistoryDb;

    public static Environment Create()
    {
        var env = new LightningEnvironment("path.db");
        env.Open();

        using var lightningTransaction = env.BeginTransaction();

        var objDb = lightningTransaction.OpenDatabase(name: "ObjectDb", new DatabaseConfiguration
        {
            Flags = DatabaseOpenFlags.Create
        });

        var histDb = lightningTransaction.OpenDatabase(name: "HistoryDb", new DatabaseConfiguration
        {
            Flags = DatabaseOpenFlags.Create
        });

        return new Environment
        {
            LightningEnvironment = env,
            ObjectDb = objDb,
            HistoryDb = histDb
        };
    }
}