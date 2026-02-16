namespace FDMF.Testing.Shared;

public static class TempDbHelper
{
    public const string TestDirectory = "TestDbs";

    public static void ClearDatabases()
    {
        if (!Directory.Exists(TestDirectory))
            return;

        // Retry deletion since LMDB may hold locks briefly even after disposal
        const int maxAttempts = 2;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                Directory.Delete(TestDirectory, recursive: true);
                return; // Success
            }
            catch (IOException)
            {
                // If this is not the final attempt, wait and retry
                if (attempt < maxAttempts - 1)
                {
                    Thread.Sleep(100);
                    continue;
                }
                // Final attempt failed - ignore as databases will be cleaned up on next run
                return;
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore permission errors - databases will be cleaned up on next run
                return;
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