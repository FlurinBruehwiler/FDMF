namespace FDMF.Testing.Shared;

public static class TempDbHelper
{
    public const string TestDirectory = "TestDbs";

    public static void ClearDatabases()
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

    public static string GetTestModelDumpFile()
    {
        return Path.Combine(AppContext.BaseDirectory, "testdata", "TestModelDump.json");
    }

    public static string GetBusinessModelDumpFile()
    {
        return Path.Combine(AppContext.BaseDirectory, "testdata", "BusinessModelDump.json");
    }
}