using FDMF.Testing.Shared;

namespace FDMF.Tests;

[CollectionDefinition(DatabaseCollectionName)]
public sealed class DatabaseCollection : IClassFixture<DatabaseCollection>
{

    public const string DatabaseCollectionName = "Database Collection";

    public DatabaseCollection()
    {
        TempDbHelper.ClearDatabases();
    }
}
