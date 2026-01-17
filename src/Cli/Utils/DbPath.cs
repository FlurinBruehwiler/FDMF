namespace Cli.Utils;

public static class DbPath
{
    public static string Resolve(DirectoryInfo? dbDir, bool allowCwd)
    {
        if (dbDir is not null)
            return dbDir.FullName;

        if (!allowCwd)
            return Path.Combine(Directory.GetCurrentDirectory(), "database");

        var cwd = Directory.GetCurrentDirectory();
        if (LooksLikeDbDirectory(cwd))
            return cwd;

        throw new Exception("No --db provided and current directory does not look like a database (missing data.mdb/lock.mdb). Provide --db <dir>.");
    }

    public static bool LooksLikeDbDirectory(string dir)
    {
        var data = Path.Combine(dir, "data.mdb");
        var lockFile = Path.Combine(dir, "lock.mdb");
        return File.Exists(data) && File.Exists(lockFile);
    }
}
