using System.Diagnostics;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
using FDMF.Testing.Shared;
using FDMF.Testing.Shared.BusinessModelModel;

namespace PerformanceTests;

public sealed class Program
{
    public static void Main()
    {
        //Ideas for performance tests:
        //Database Read
        //Database Write
        //Path Evaluation

        //For each tests we also want some scaling parameter, so that we can see if the time scales linearly/log/exponential

        try
        {
            var objCount = 100_000;

            TempDbHelper.ClearDatabases();

            //warmup
            Write(objCount);

            TempDbHelper.ClearDatabases();

            var s = Stopwatch.GetTimestamp();

            //actual
            Write(objCount);

            Console.WriteLine($"Write with {objCount} objects took {Stopwatch.GetElapsedTime(s).TotalMilliseconds}ms");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    public static void Write(int objectCount)
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetBusinessModelDumpFile());
        using var session = new DbSession(env, arenaSize: 100_000_000);

        var user = new User(session);

        for (int i = 0; i < objectCount; i++)
        {
            var document = new Document(session);
            document.CreatedAt = DateTime.Now;
            document.FileSize = 1000;
            document.Title = "Testing Folder";
            document.State = "Active";
            document.CreatedBy = user;
        }
    }
}