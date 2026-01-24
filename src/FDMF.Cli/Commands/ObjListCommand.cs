using System.CommandLine;
using FDMF.Cli.Utils;
using FDMF.Core.Database;
using Model.Generated;
using Environment = FDMF.Core.Environment;

namespace FDMF.Cli.Commands;

public static class ObjListCommand
{
    public static Command Build()
    {
        var typeOption = new Option<string?>("--type", "Optional entity type key (e.g. Folder)");
        var limitOption = new Option<int>("--limit", () => 200, "Max objects to list");

        var cmd = new Command("list", "List objects (optionally filtered by type)");
        cmd.AddOption(CliOptions.Db);
        cmd.AddOption(typeOption);
        cmd.AddOption(limitOption);

        cmd.SetHandler((DirectoryInfo? dbDir, string? typeKey, int limit) =>
        {
            var resolvedDb = DbPath.Resolve(dbDir, allowCwd: true);

            Guid? filterTypId = null;
            string[]? scalarKeys = null;
            string[]? refKeys = null;
            FieldDefinition[]? scalarFields = null;
            ReferenceFieldDefinition[]? refFields = null;

            using var env = Environment.Open(resolvedDb);
            using var session = new DbSession(env, readOnly: true);

            if (!string.IsNullOrWhiteSpace(typeKey))
            {
                var entity = ModelLookup.GetType(session, typeKey);
                filterTypId = Guid.Parse(entity.Id);
                scalarFields = entity.FieldDefinitions.ToArray();
                refFields = entity.ReferenceFieldDefinitions.ToArray();
                scalarKeys = scalarFields.Select(f => f.Key).ToArray();
                refKeys = refFields.Select(f => f.Key).ToArray();
            }

            var headers = new List<string> { "ObjId", "Type" };
            if (scalarKeys is not null) headers.AddRange(scalarKeys);
            if (refKeys is not null) headers.AddRange(refKeys);

            var rows = new List<string[]>();

            int count = 0;
            foreach (var (objId, typId) in session.EnumerateObjs())
            {
                if (filterTypId.HasValue && typId != filterTypId.Value)
                    continue;

                if (!filterTypId.HasValue)
                {
                    rows.Add([objId.ToString(), ModelLookup.FormatType(session, typId)]);
                    count++;
                    if (count >= limit) break;
                    continue;
                }

                var row = new string[headers.Count];
                row[0] = objId.ToString();
                row[1] = ModelLookup.FormatType(session, typId);

                int col = 2;
                foreach (var fld in scalarFields!)
                {
                    var bytes = session.GetFldValue(objId, Guid.Parse(fld.Id));
                    row[col++] = EncodingUtils.DecodeScalar(Enum.Parse<FieldDataType>(fld.DataType), bytes);
                }

                foreach (var rf in refFields!)
                {
                    row[col++] = DecodeRefValue(session, objId, rf);
                }

                rows.Add(row);
                count++;
                if (count >= limit)
                    break;
            }

            TablePrinter.Print(headers, rows);
        }, CliOptions.Db, typeOption, limitOption);

        return cmd;
    }

    private static string DecodeRefValue(DbSession session, Guid objId, ReferenceFieldDefinition rf)
    {
        if (rf.RefType == nameof(RefType.Multiple))
        {
            var ids = new List<Guid>();
            foreach (var other in session.EnumerateAso(objId, Guid.Parse(rf.Id)))
            {
                ids.Add(other.ObjId);
                if (ids.Count >= 3)
                    break;
            }

            if (ids.Count == 0)
                return string.Empty;

            var count = session.GetAsoCount(objId, Guid.Parse(rf.Id));
            if (count <= 2)
                return string.Join(", ", ids.Select(x => x.ToString()));

            var shown = ids.Take(2).Select(x => x.ToString());
            return $"{string.Join(", ", shown)} and {count - 2} more";
        }

        var otherId = session.GetSingleAsoValue(objId, Guid.Parse(rf.Id));
        return otherId?.ToString() ?? string.Empty;
    }
}
