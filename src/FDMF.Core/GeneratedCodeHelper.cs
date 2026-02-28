using System.Collections;
using FDMF.Core.DatabaseLayer;

namespace FDMF.Core;

public static class GeneratedCodeHelper
{
    public static bool IsAssignableFrom(DbSession dbSession, Guid baseTypId, Guid derivedTypId)
    {
        if (baseTypId == Guid.Empty || derivedTypId == Guid.Empty)
            return false;

        if (baseTypId == derivedTypId)
            return true;

        // Walk derived -> parent chain.
        var seen = new HashSet<Guid>();
        var current = derivedTypId;
        while (seen.Add(current))
        {
            var ed = dbSession.GetObjFromGuid<EntityDefinition>(current);
            if (!ed.HasValue)
                return false;

            var parent = ed.Value.Parent;
            if (!parent.HasValue)
                return false;

            current = parent.Value.ObjId;
            if (current == baseTypId)
                return true;
        }

        // Cycle in inheritance graph.
        return false;
    }

    public static T? GetNullableAssoc<T>(DbSession dbSession, Guid objId, Guid fldId) where T : struct, ITransactionObject
    {
        var asoValue = dbSession.GetSingleAsoValue(objId, fldId);
        if (!asoValue.HasValue)
            return null;

        return new T { DbSession = dbSession, ObjId = asoValue.Value };
    }

    public static T GetAssoc<T>(DbSession dbSession, Guid objId, Guid fldId) where T : struct, ITransactionObject
    {
        var asoValue = dbSession.GetSingleAsoValue(objId, fldId);
        if (!asoValue.HasValue)
            throw new Exception("what should happen here?");

        return new T { DbSession = dbSession, ObjId = asoValue.Value };
    }

    public static void SetAssoc(DbSession dbSession, Guid objIdA, Guid fldIdA, Guid objIdB, Guid fldIdB)
    {
        if (objIdB == Guid.Empty)
        {
            dbSession.RemoveAllAso(objIdA, fldIdA);
        }
        else
        {
            dbSession.CreateAso(objIdA, fldIdA, objIdB, fldIdB);
        }
    }
}

public struct AssoCollectionEnumerator<T> : IEnumerator<T> where T : struct, ITransactionObject
{
    private readonly DbSession _dbSession;
    private AsoFldEnumerator _asoFldEnumerator;
    private T _current;

    public AssoCollectionEnumerator(DbSession dbSession, AsoFldEnumerator asoFldEnumerator)
    {
        _dbSession = dbSession;
        _asoFldEnumerator = asoFldEnumerator;
    }

    public void Dispose()
    {
        _asoFldEnumerator.Dispose();
    }

    public bool MoveNext()
    {
        var result = _asoFldEnumerator.MoveNext();

        if (result)
        {
            _current = default;
            _current.DbSession = _dbSession;
            _current.ObjId = _asoFldEnumerator.Current.ObjId;
        }

        return result;
    }

    public void Reset()
    {
        throw new NotImplementedException();
    }

    public T Current => _current;

    T IEnumerator<T>.Current => _current;

    object IEnumerator.Current => throw new NotImplementedException();
}

public struct AssocCollection<T> : ICollection<T> where T : struct, ITransactionObject
{
    public Guid _objId;
    public Guid _fldId;
    public Guid _otherFld;
    public DbSession DbSession;

    public AssocCollection(DbSession dbSession, Guid objId, Guid fldId, Guid otherFld)
    {
        DbSession = dbSession;
        _objId = objId;
        _fldId = fldId;
        _otherFld = otherFld;
    }

    public AssoCollectionEnumerator<T> GetEnumerator()
    {
        return new AssoCollectionEnumerator<T>(DbSession, DbSession.EnumerateAso(_objId, _fldId).GetEnumerator());
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Add(T item)
    {
        DbSession.CreateAso(_objId, _fldId, item.ObjId, _otherFld);
    }

    public void Clear()
    {
        DbSession.RemoveAllAso(_objId, _fldId);
    }

    public bool Contains(T item)
    {
        foreach (var otherObj in DbSession.EnumerateAso(_objId, _fldId))
        {
            if (otherObj.ObjId == item.ObjId)
                return true;
        }

        return false;
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        var i = arrayIndex;
        foreach (var t in this)
        {
            array[i] = t;
            i++;
        }
    }

    public bool Remove(T item)
    {
        return DbSession.RemoveAso(_objId, _fldId, item.ObjId, _otherFld);
    }

    public int Count => DbSession.GetAsoCount(_objId, _fldId);
    public bool IsReadOnly => false;

}
