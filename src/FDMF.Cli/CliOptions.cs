using System.CommandLine;

namespace FDMF.Cli;

public static class CliOptions
{
    public static readonly Option<DirectoryInfo?> Db = new("--db", description: "Database directory (defaults to current directory if it looks like a db)");
}
