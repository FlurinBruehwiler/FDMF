using System.CommandLine;
using FDMF.Cli.Utils;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

namespace FDMF.Cli.Commands;

public static class ObjCreateCommand
{
    public static Command Build()
    {
        var setOption = new Option<string[]>("--set", description: "Set a field: Key=Value. Scalar fields take literal values; reference fields take ObjIds. Use Key= to clear.")
        {
            AllowMultipleArgumentsPerToken = true
        };

        var typeArg = new Argument<string>("type");

        var cmd = new Command("create", "Create a new object of a given type")
        {
            typeArg
        };

        cmd.AddOption(CliOptions.Db);
        cmd.AddOption(setOption);

        cmd.SetHandler((string typeKey, DirectoryInfo? dbDir, string[] setPairs) =>
        {
            return Task.Run(() =>
            {
                var resolvedDb = DbPath.Resolve(dbDir, allowCwd: true);

                using var env = DbEnvironment.Open(resolvedDb);
                using var session = new DbSession(env);

                var entity = ModelLookup.GetType(session, typeKey);

                var objId = session.CreateObj(entity.Id);

                ObjectMutations.ApplySets(session, entity, objId, setPairs ?? Array.Empty<string>(), ObjectMutations.MultiRefMode.Add);

                session.Commit();
                Console.WriteLine(objId);
            });
        }, typeArg, CliOptions.Db, setOption);

        return cmd;
    }
}
