using FDMF.Core;
using FDMF.Core.DatabaseLayer;
using FDMF.Testing.Shared;
using FDMF.Testing.Shared.BusinessModelModel;

namespace PerformanceTests;

public class TraversingAssocsTest : IPerformanceTest
{
    public static int[] Counts { get; } = [100, 1000, 10_000];

    private DbEnvironment _env;
    private Guid _startingFolder;

    public TraversingAssocsTest()
    {
        _env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetBusinessModelDumpFile());
        using var session = new DbSession(_env, arenaSize: 100_000_000);

        List<Folder> folders = new List<Folder>(1_000);

        //create 1k folders
        for (int i = 0; i < 1_000; i++)
        {
            folders.Add(new Folder(session));
        }

        //create 1k connections
        var current = Remove(0);
        _startingFolder = current.ObjId;

        var rand = new Random(42);

        //creating a random circular chain 900 assocs long
        for (int i = 900; i > 0; i--)
        {
            var idx = rand.Next(0, i);
            var folder = Remove(idx);
            current.Parent = folder;
            current = folder;
        }

        current.Parent = session.GetObjFromGuid<Folder>(_startingFolder);

        session.Commit();

        Folder Remove(int index)
        {
            var folder = folders[index];
            var last = folders[^1];
            folders[index] = last;
            folders.RemoveAt(folders.Count - 1);
            return folder;
        }
    }

    public void Run(int count)
    {
        using var session = new DbSession(_env, readOnly: true);

        var allFolders = Searcher.Search<Folder>(session);
        Console.WriteLine(allFolders.Count);

        var current = session.GetObjFromGuid<Folder>(_startingFolder)!.Value;
        for (int i = 0; i < count; i++)
        {
            current = current.Parent!.Value;
        }
    }

    public void Dispose()
    {
        _env.Dispose();
    }
}