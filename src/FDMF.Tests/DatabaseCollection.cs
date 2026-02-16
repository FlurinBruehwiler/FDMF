using FDMF.Testing.Shared;

namespace FDMF.Tests;

[CollectionDefinition(DatabaseCollectionName)]
public sealed class DatabaseCollection : IClassFixture<DatabaseCollection>, IDisposable
{

    public const string DatabaseCollectionName = "Database Collection";

    public DatabaseCollection()
    {
        TempDbHelper.ClearDatabases();
    }

    public void Dispose()
    {
        // Ensure all test databases are cleaned up after test collection completes
        try
        {
            TempDbHelper.ClearDatabases();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore cleanup errors to avoid masking test failures
            // Database files may still be in use or locked
        }
    }
}
