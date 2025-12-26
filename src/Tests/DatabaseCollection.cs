namespace Tests;

[CollectionDefinition(DatabaseCollectionName)]
public class DatabaseCollection : IClassFixture<DatabaseCollection>
{
    public const string TestDirectory = "TestDbs";

    public const string DatabaseCollectionName = "Database Collection";

    public DatabaseCollection()
    {
        if (Directory.Exists(TestDirectory))
        {
            Directory.Delete(TestDirectory, recursive: true);
        }
    }

    public static string GetTempDbDirectory()
    {
        return Path.Combine(TestDirectory, Guid.NewGuid().ToString("N"));
    }
}