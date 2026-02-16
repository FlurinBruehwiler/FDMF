using FDMF.Core;

namespace FDMF.Tests;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class DatabaseFixture
{
    public DatabaseFixture()
    {
        DbEnvironment.IsTesting = true;
    }
}

[CollectionDefinition(DatabaseCollectionName)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string DatabaseCollectionName = "Database Collection";
}
