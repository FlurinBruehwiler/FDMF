namespace FDMF.Testing.Shared;

public static class TempDbHelper
{
    private const string TestDirectory = "TestDbs";

    public static string GetTempDbDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, TestDirectory, Guid.NewGuid().ToString("N"));
    }

    public static TempDbDisposable CreateTempDbDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, TestDirectory, Guid.NewGuid().ToString("N"));
        return new TempDbDisposable
        {
            Dir = path,
        };
    }

    public static string GetTestModelDumpFile()
    {
        return Path.Combine(AppContext.BaseDirectory, "testdata", "TestModelDump.json");
    }

    public static string GetBusinessModelDumpFile()
    {
        return Path.Combine(AppContext.BaseDirectory, "testdata", "BusinessModelDump.json");
    }

    public struct TempDbDisposable : IDisposable
    {
        public string Dir;

        public void Dispose()
        {
            Directory.Delete(Dir, recursive: true);
        }
    }
}