using BaseModel.Generated;

namespace FDMF.Core.PathLayer;

public sealed class PathLangModelIndex
{
    public sealed record ScalarFieldInfo(string Key, Guid FldId, string DataType);

    public sealed record AssocFieldInfo(string Key, Guid FldId, Guid TargetTypId, string RefType);

    public sealed record EntityInfo(
        string Key,
        Guid TypId,
        Dictionary<string, ScalarFieldInfo> ScalarFields,
        Dictionary<string, AssocFieldInfo> AssocFields,
        List<Guid> DirectParents
    );

    private readonly Dictionary<string, EntityInfo> _entityByKey;
    private readonly Dictionary<Guid, EntityInfo> _entityByTypId;
    private readonly Dictionary<Guid, List<Guid>> _ancestorsCache = new();

    private PathLangModelIndex(Dictionary<string, EntityInfo> entityByKey, Dictionary<Guid, EntityInfo> entityByTypId)
    {
        _entityByKey = entityByKey;
        _entityByTypId = entityByTypId;
    }

    public static PathLangModelIndex Create(Model model)
    {
        var entityByKey = new Dictionary<string, EntityInfo>(StringComparer.Ordinal);
        var entityByTypId = new Dictionary<Guid, EntityInfo>();

        // We include imported models too.
        foreach (var ed in EnumerateAllEntityDefinitions(model))
        {
            if (!Guid.TryParse(ed.Id, out var typId))
                continue;

            var scalarFields = new Dictionary<string, ScalarFieldInfo>(StringComparer.Ordinal);
            foreach (var fd in ed.FieldDefinitions)
            {
                if (!Guid.TryParse(fd.Id, out var fldId))
                    continue;
                scalarFields[fd.Key] = new ScalarFieldInfo(fd.Key, fldId, fd.DataType);
            }

            var assocFields = new Dictionary<string, AssocFieldInfo>(StringComparer.Ordinal);
            foreach (var rd in ed.ReferenceFieldDefinitions)
            {
                if (!Guid.TryParse(rd.Id, out var fldId))
                    continue;

                // Target type is the owning entity of the opposite reference field.
                Guid targetTypId = Guid.Empty;
                try
                {
                    var otherOwning = rd.OtherReferenceFields.OwningEntity;
                    Guid.TryParse(otherOwning.Id, out targetTypId);
                }
                catch
                {
                    // Ignore broken links.
                }

                assocFields[rd.Key] = new AssocFieldInfo(rd.Key, fldId, targetTypId, rd.RefType);
            }

            var parents = new List<Guid>();
            foreach (var p in ed.Parents)
            {
                if (Guid.TryParse(p.Id, out var pid))
                    parents.Add(pid);
            }

            var info = new EntityInfo(ed.Key, typId, scalarFields, assocFields, parents);
            entityByKey[ed.Key] = info;
            entityByTypId[typId] = info;
        }

        return new PathLangModelIndex(entityByKey, entityByTypId);
    }

    private static IEnumerable<EntityDefinition> EnumerateAllEntityDefinitions(Model model)
    {
        var visitedModels = new HashSet<Guid>();
        var stack = new Stack<Model>();
        stack.Push(model);

        while (stack.Count > 0)
        {
            var m = stack.Pop();
            if (!visitedModels.Add(m.ObjId))
                continue;

            foreach (var ed in m.EntityDefinitions)
                yield return ed;

            foreach (var imported in m.ImportedModels)
                stack.Push(imported);
        }
    }

    public bool TryGetEntityByKey(string key, out EntityInfo info)
    {
        return _entityByKey.TryGetValue(key, out info!);
    }

    public bool TryGetEntityByTypId(Guid typId, out EntityInfo info)
    {
        return _entityByTypId.TryGetValue(typId, out info!);
    }

    public IEnumerable<Guid> GetSelfAndAncestors(Guid typId)
    {
        if (_ancestorsCache.TryGetValue(typId, out var cached))
            return cached;

        var list = new List<Guid>();
        var visited = new HashSet<Guid>();

        void Visit(Guid id)
        {
            if (id == Guid.Empty)
                return;
            if (!visited.Add(id))
                return;
            list.Add(id);

            if (!_entityByTypId.TryGetValue(id, out var e))
                return;
            for (int i = 0; i < e.DirectParents.Count; i++)
                Visit(e.DirectParents[i]);
        }

        Visit(typId);
        _ancestorsCache[typId] = list;
        return list;
    }

    public bool TryResolveScalar(Guid typId, string fieldKey, out ScalarFieldInfo info)
    {
        foreach (var t in GetSelfAndAncestors(typId))
        {
            if (_entityByTypId.TryGetValue(t, out var e) && e.ScalarFields.TryGetValue(fieldKey, out info!))
                return true;
        }

        info = default!;
        return false;
    }

    public bool TryResolveAssoc(Guid typId, string assocKey, out AssocFieldInfo info)
    {
        foreach (var t in GetSelfAndAncestors(typId))
        {
            if (_entityByTypId.TryGetValue(t, out var e) && e.AssocFields.TryGetValue(assocKey, out info!))
                return true;
        }

        info = default!;
        return false;
    }
}
