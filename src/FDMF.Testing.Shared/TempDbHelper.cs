namespace FDMF.Testing.Shared;

public static class TempDbHelper
{
    public const string TestDirectory = "TestDbs";

    public static void ClearDatabases()
    {
        if (!Directory.Exists(TestDirectory))
            return;

        const int maxAttempts = 2;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                Directory.Delete(TestDirectory, recursive: true);
                return; // Success
            }
            catch (IOException) when (attempt < maxAttempts - 1)
            {
                // If deletion fails (e.g., files still in use), try again after a short delay
                Thread.Sleep(100);
            }
            catch (IOException)
            {
                // Final attempt failed - ignore as databases will be cleaned up on next run
                // This can happen if database files are still locked by the OS
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