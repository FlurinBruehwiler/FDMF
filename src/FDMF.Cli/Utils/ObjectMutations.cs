using FDMF.Core.DatabaseLayer;

namespace FDMF.Cli.Utils;

public static class ObjectMutations
{
    public enum MultiRefMode
    {
        Add,
        Replace
    }

    public static void ApplySets(DbSession session, EntityDefinition entity, Guid objId, IEnumerable<string> setPairs, MultiRefMode multiRefMode)
    {
        foreach (var pair in setPairs ?? Array.Empty<string>())
        {
            var (k, v) = Pairs.Split(pair);

            if (ModelLookup.GetFld(entity, k) is {} fld)
            {
                if (v.Length == 0)
                {
                    session.SetFldValue(objId, Guid.Parse(fld.Id), ReadOnlySpan<byte>.Empty);
                }
                else
                {
                    var bytes = EncodingUtils.EncodeScalar(Enum.Parse<FieldDataType>(fld.DataType), v);
                    session.SetFldValue(objId, Guid.Parse(fld.Id), bytes);
                }

                continue;
            }

            if (ModelLookup.GetRefFld(entity, k) is {} rf)
            {
                if (v.Length == 0)
                {
                    session.RemoveAllAso(objId, Guid.Parse(rf.Id));
                    continue;
                }

                var parts = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 0)
                    continue;

                if (rf.RefType is nameof(RefType.SingleMandatory) or nameof(RefType.SingleOptional))
                {
                    if (parts.Length != 1)
                        throw new Exception($"Reference field '{k}' is single-valued. Provide exactly one ObjId.");

                    if (!Guid.TryParse(parts[0], out var otherObjId))
                        throw new Exception($"Invalid guid '{parts[0]}' for ref field '{k}'");

                    session.RemoveAllAso(objId, Guid.Parse(rf.Id));
                    if (otherObjId != Guid.Empty)
                        session.CreateAso(objId, Guid.Parse(rf.Id), otherObjId, Guid.Parse(rf.OtherReferenceFields.Id));
                }
                else
                {
                    if (multiRefMode == MultiRefMode.Replace)
                        session.RemoveAllAso(objId, Guid.Parse(rf.Id));

                    foreach (var part in parts)
                    {
                        if (!Guid.TryParse(part, out var otherObjId))
                            throw new Exception($"Invalid guid '{part}' for ref field '{k}'");

                        if (otherObjId != Guid.Empty)
                            session.CreateAso(objId, Guid.Parse(rf.Id), otherObjId, Guid.Parse(rf.OtherReferenceFields.Id));
                    }
                }

                continue;
            }

            throw new Exception($"Unknown field '{k}' for type '{entity.Key}'");
        }

        ValidateMandatorySingleRefs(session, entity, objId);
    }

    public static void ValidateMandatorySingleRefs(DbSession session, EntityDefinition entity, Guid objId)
    {
        foreach (var rf in entity.ReferenceFieldDefinitions)
        {
            if (rf.RefType != nameof(RefType.SingleMandatory))
                continue;

            var val = session.GetSingleAsoValue(objId, Guid.Parse(rf.Id));
            if (!val.HasValue)
                throw new Exception($"Missing mandatory reference field '{rf.Key}' for type '{entity.Key}'.");
        }
    }
}
