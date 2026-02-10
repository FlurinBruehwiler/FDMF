using System.CommandLine;
using FDMF.Cli.Utils;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

namespace FDMF.Cli.Commands;

public static class ObjUpdateCommand
{
    public static Command Build()
    {
        var setOption = new Option<string[]>("--set", description: "Set a field: Key=Value. Scalar fields take literal values; reference fields take ObjIds. Use Key= to clear.")
        {
            AllowMultipleArgumentsPerToken = true
        };

        var objIdArg = new Argument<Guid>("objId");

        var cmd = new Command("update", "Update an existing object")
        {
            objIdArg
        };

        cmd.AddOption(CliOptions.Db);
        cmd.AddOption(setOption);

        cmd.SetHandler((Guid objId, DirectoryInfo? dbDir, string[] setPairs) =>
        {
            return Task.Run(() =>
            {
                var resolvedDb = DbPath.Resolve(dbDir, allowCwd: true);

                using var env = DbEnvironment.Open(resolvedDb);
                using var session = new DbSession(env);

                var typId = session.GetTypId(objId);
                if (typId == Guid.Empty)
                    throw new Exception($"Object '{objId}' not found");

                var entity = session.GetObjFromGuid<EntityDefinition>(typId);
                if (entity is null)
                    throw new Exception($"Unknown type id '{typId}' for object '{objId}'");

                ObjectMutations.ApplySets(session, entity.Value, objId, setPairs, ObjectMutations.MultiRefMode.Replace);

                session.Commit();
                Console.WriteLine(objId);
            });
        }, objIdArg, CliOptions.Db, setOption);

        return cmd;
    }
}
