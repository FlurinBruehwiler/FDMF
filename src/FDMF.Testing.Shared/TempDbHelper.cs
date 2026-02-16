namespace FDMF.Testing.Shared;

public static class TempDbHelper
{
    public const string TestDirectory = "TestDbs";

    public static void ClearDatabases()
    {
        if (Directory.Exists(TestDirectory))
        {
            try
            {
                Directory.Delete(TestDirectory, recursive: true);
            }
            catch (IOException)
            {
                // If deletion fails (e.g., files still in use), try again after a short delay
                Thread.Sleep(100);
                try
                {
                    Directory.Delete(TestDirectory, recursive: true);
                }
                catch
                {
                    // Ignore if still can't delete - databases will be cleaned up on next run
                }
            }
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