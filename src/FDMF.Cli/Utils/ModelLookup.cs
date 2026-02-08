using FDMF.Core.DatabaseLayer;

namespace FDMF.Cli.Utils;

public static class ModelLookup
{
    public static ReferenceFieldDefinition? GetRefFld(EntityDefinition entityDefinition, string fldKey)
    {
        foreach (var fld in entityDefinition.ReferenceFieldDefinitions)
        {
            if (fld.Key == fldKey)
            {
                return fld;
            }
        }

        foreach (var ped in entityDefinition.Parents)
        {
            var r = GetRefFld(ped, fldKey);
            if (r != null)
                return r;
        }

        return null;
    }

    public static FieldDefinition? GetFld(EntityDefinition entityDefinition, string fldKey)
    {
        foreach (var fld in entityDefinition.FieldDefinitions)
        {
            if (fld.Key == fldKey)
            {
                return fld;
            }
        }

        foreach (var ped in entityDefinition.Parents)
        {
            var r = GetFld(ped, fldKey);
            if (r != null)
                return r;
        }

        return null;
    }

    public static EntityDefinition GetType(DbSession session, string key)
    {
        return Searcher.Search<EntityDefinition>(session, new StringCriterion
        {
            FieldId = EntityDefinition.Fields.Key,
            Value = key,
            Type = StringCriterion.MatchType.Exact
        }).First();
    }

    public static string FormatType(DbSession session, Guid typId)
    {
        return session.GetObjFromGuid<EntityDefinition>(typId)?.Key ?? typId.ToString();
    }
}
