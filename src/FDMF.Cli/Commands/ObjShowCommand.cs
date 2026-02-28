using System.CommandLine;
using FDMF.Cli.Utils;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

namespace FDMF.Cli.Commands;

public static class ObjShowCommand
{
    public static Command Build()
    {
        var objIdArg = new Argument<Guid>("objId");

        var cmd = new Command("show", "Show a single object including all references")
        {
            objIdArg
        };

        cmd.AddOption(CliOptions.Db);

        cmd.SetHandler((Guid objId, DirectoryInfo? dbDir) =>
        {
            return Task.Run(() =>
            {
                var resolvedDb = DbPath.Resolve(dbDir, allowCwd: true);

                using var env = DbEnvironment.Open(resolvedDb);
                using var session = new DbSession(env, readOnly: true);

                var typId = session.GetTypId(objId);
                if (typId == Guid.Empty)
                    throw new Exception($"Object '{objId}' not found");

                var entity = session.GetObjFromGuid<EntityDefinition>(typId);
                var typeName = entity?.Key ?? typId.ToString();

                Console.WriteLine($"ObjId: {objId}");
                Console.WriteLine($"Type:  {typeName}");

                if (entity is null)
                    return;

                foreach (var fld in entity.Value.FieldDefinitions)
                {
                    var bytes = session.GetFldValue(objId, fld.Id);
                    var v = EncodingUtils.DecodeScalar(fld.DataType, bytes);
                    Console.WriteLine($"{fld.Key}: {v}");
                }

                foreach (var rf in entity.Value.ReferenceFieldDefinitions)
                {
                    Console.WriteLine($"{rf.Key}:");

                    if (rf.RefType == RefType.Multiple)
                    {
                        int shown = 0;
                        foreach (var other in session.EnumerateAso(objId, rf.Id))
                        {
                            Console.WriteLine($"  - {other.ObjId} ({ModelLookup.FormatType(session, session.GetTypId(other.ObjId))})");
                            shown++;
                        }

                        if (shown == 0)
                            Console.WriteLine("  (empty)");
                    }
                    else
                    {
                        var otherId = session.GetSingleAsoValue(objId, rf.Id);
                        if (!otherId.HasValue)
                        {
                            Console.WriteLine("  (empty)");
                        }
                        else
                        {
                            Console.WriteLine($"  - {otherId.Value} ({ModelLookup.FormatType(session, session.GetTypId(otherId.Value))})");
                        }
                    }
                }
            });
        }, objIdArg, CliOptions.Db);

        return cmd;
    }
}
