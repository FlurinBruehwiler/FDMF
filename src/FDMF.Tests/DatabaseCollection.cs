using FDMF.Testing.Shared;

namespace FDMF.Tests;

[CollectionDefinition(DatabaseCollectionName)]
public sealed class DatabaseCollection : IClassFixture<DatabaseCollection>
{

    public const string DatabaseCollectionName = "Database Collection";

    public DatabaseCollection()
    {
        // Don't clear databases here - each test uses its own unique directory via GetTempDbDirectory()
        // which returns TestDbs/{Guid}/, providing automatic isolation.
        // Calling ClearDatabases() here interferes with parallel test execution.
    }
}
