using System.CommandLine;
using Cli.Utils;
using Shared.Database;
using Environment = Shared.Environment;

namespace Cli.Commands;

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
                var model = ModelLoader.Load();

                using var env = Environment.Open(model, resolvedDb);
                using var session = new DbSession(env);

                var typId = session.GetTypId(objId);
                if (typId == Guid.Empty)
                    throw new Exception($"Object '{objId}' not found");

                var entity = model.EntityDefinitions.FirstOrDefault(e => e.Id == typId);
                if (entity is null)
                    throw new Exception($"Unknown type id '{typId}' for object '{objId}'");

                var scalarByKey = entity.Fields.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
                var refByKey = entity.ReferenceFields.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

                foreach (var pair in setPairs ?? Array.Empty<string>())
                {
                    var (k, v) = Pairs.Split(pair);

                    if (scalarByKey.TryGetValue(k, out var fld))
                    {
                        if (v.Length == 0)
                        {
                            session.SetFldValue(objId, fld.Id, ReadOnlySpan<byte>.Empty);
                        }
                        else
                        {
                            var bytes = EncodingUtils.EncodeScalar(fld.DataType, v);
                            session.SetFldValue(objId, fld.Id, bytes);
                        }

                        continue;
                    }

                    if (refByKey.TryGetValue(k, out var rf))
                    {
                        if (v.Length == 0)
                        {
                            session.RemoveAllAso(objId, rf.Id);
                            continue;
                        }

                        var parts = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (parts.Length == 0)
                            continue;

                        if (rf.RefType is Shared.RefType.SingleMandatory or Shared.RefType.SingleOptional)
                        {
                            if (parts.Length != 1)
                                throw new Exception($"Reference field '{k}' is single-valued. Provide exactly one ObjId.");

                            if (!Guid.TryParse(parts[0], out var otherObjId))
                                throw new Exception($"Invalid guid '{parts[0]}' for ref field '{k}'");

                            session.RemoveAllAso(objId, rf.Id);
                            if (otherObjId != Guid.Empty)
                                session.CreateAso(objId, rf.Id, otherObjId, rf.OtherReferenceField.Id);
                        }
                        else
                        {
                            // For multi refs, treat --set Field=a,b,c as "replace".
                            session.RemoveAllAso(objId, rf.Id);

                            foreach (var part in parts)
                            {
                                if (!Guid.TryParse(part, out var otherObjId))
                                    throw new Exception($"Invalid guid '{part}' for ref field '{k}'");

                                if (otherObjId != Guid.Empty)
                                    session.CreateAso(objId, rf.Id, otherObjId, rf.OtherReferenceField.Id);
                            }
                        }

                        continue;
                    }

                    throw new Exception($"Unknown field '{k}' for type '{entity.Key}'");
                }

                foreach (var rf in entity.ReferenceFields)
                {
                    if (rf.RefType != Shared.RefType.SingleMandatory)
                        continue;

                    var val = session.GetSingleAsoValue(objId, rf.Id);
                    if (!val.HasValue)
                        throw new Exception($"Missing mandatory reference field '{rf.Key}' for type '{entity.Key}'.");
                }

                session.Commit();
                Console.WriteLine(objId);
            });
        }, objIdArg, CliOptions.Db, setOption);

        return cmd;
    }
}
