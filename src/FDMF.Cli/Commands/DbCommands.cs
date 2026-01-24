using System.CommandLine;
using FDMF.Cli.Utils;
using Environment = FDMF.Core.Environment;

namespace FDMF.Cli.Commands;

public static class DbCommands
{
    public static Command Build()
    {
        var db = new Command("db", "Database commands");

        var forceOption = new Option<bool>("--force", "Delete existing directory if it exists");

        var init = new Command("init", "Initialize a new database directory");
        init.AddOption(CliOptions.Db);
        init.AddOption(forceOption);

        init.SetHandler((DirectoryInfo? dbDir, bool force) =>
        {
            var resolvedDb = DbPath.Resolve(dbDir, allowCwd: false);

            if (Directory.Exists(resolvedDb))
            {
                if (!force)
                    throw new Exception($"Database directory '{resolvedDb}' already exists. Pass --force to delete it.");

                Directory.Delete(resolvedDb, recursive: true);
            }

            using var _ = Environment.CreateDatabase(resolvedDb);

            Console.WriteLine($"Initialized database at '{resolvedDb}'.");
        }, CliOptions.Db, forceOption);

        db.AddCommand(init);
        db.AddCommand(DbDumpJsonCommand.Build());
        db.AddCommand(DbLoadJsonCommand.Build());
        return db;
    }
}
