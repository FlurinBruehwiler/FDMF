using System.CommandLine;
using FDMF.Cli.Utils;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
using Environment = FDMF.Core.Environment;

namespace FDMF.Cli.Commands;

public static class TypesCommand
{
    public static Command Build()
    {
        var cmd = new Command("types", "List entity types");

        cmd.SetHandler((DirectoryInfo? dbDir) =>
        {
            var resolvedDb = DbPath.Resolve(dbDir, allowCwd: true);

            using var env = Environment.Open(resolvedDb);
            using var session = new DbSession(env);

            var mdl = session.GetObjFromGuid<Model>(env.ModelGuid);

            foreach (var e in Enumerable.OrderBy<EntityDefinition, string>(mdl!.Value.GetAllEntityDefinitions(), x => x.Key))
            {
                Console.WriteLine($"{e.Key}\t{e.Id}");
            }
        }, CliOptions.Db);

        return cmd;
    }
}
