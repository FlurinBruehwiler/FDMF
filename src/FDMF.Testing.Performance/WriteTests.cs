using FDMF.Core;
using FDMF.Core.DatabaseLayer;
using FDMF.Testing.Shared;
using FDMF.Testing.Shared.BusinessModelModel;

namespace PerformanceTests;

public class WriteTests : IPerformanceTest
{
    public static int[] Counts { get; } = [100, 1000, 10_000];

    private DbEnvironment _env;
    private DbSession _session;

    public WriteTests()
    {
        _env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetBusinessModelDumpFile());
        _session = new DbSession(_env, arenaSize: 100_000_000); //todo maybe switch to a linked list based arena, so we don't have do reserve huge chunks of memory
    }

    public void Run(int count)
    {
        var user = new User(_session);

        for (int i = 0; i < count; i++)
        {
            var document = new Document(_session);
            document.CreatedAt = DateTime.Now;
            document.FileSize = 1000;
            document.Title = "Testing Folder";
            document.State = "Active";
            document.CreatedBy = user;
        }

        _session.Commit();
    }

    public void Dispose()
    {
        _session.Dispose();
        _env.Dispose();
    }
}