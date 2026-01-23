using System.CommandLine;
using Cli.Utils;
using Shared.Database;
using Environment = Shared.Environment;

namespace Cli.Commands;

public static class DbLoadJsonCommand
{
    public static Command Build()
    {
        var fileArg = new Argument<FileInfo>("file", description: "JSON file to import (format produced by db dump-json)");

        var cmd = new Command("load-json", "Load/update objects from a JSON dump")
        {
            TreatUnmatchedTokensAsErrors = true
        };

        cmd.AddArgument(fileArg);

        cmd.AddOption(CliOptions.Db);

        cmd.SetHandler((FileInfo file, DirectoryInfo? dbDir) =>
        {
            if (!file.Exists)
                throw new Exception($"Input file not found: '{file.FullName}'");

            var resolvedDb = DbPath.Resolve(dbDir, allowCwd: true);

            using var env = Environment.Open(resolvedDb);
            using var session = new DbSession(env);

            var json = File.ReadAllText(file.FullName);
            JsonDump.FromJson(json, session);

            session.Commit();
            Console.WriteLine($"Imported JSON from '{file.FullName}'.");
        }, fileArg, CliOptions.Db);

        return cmd;
    }
}
