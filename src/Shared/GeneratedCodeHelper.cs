using System.Collections;

namespace Model;



public static class GeneratedCodeHelper
{
    public static T? GetNullableAssoc<T>(Transaction transaction, Guid objId, Guid fldId) where T : struct, ITransactionObject
    {
        var asoValue = transaction.GetSingleAsoValue(objId, fldId);
        if (!asoValue.HasValue)
            return null;

        return new T { _transaction = transaction, _objId = asoValue.Value };
    }

    public static T GetAssoc<T>(Transaction transaction, Guid objId, Guid fldId) where T : struct, ITransactionObject
    {
        var asoValue = transaction.GetSingleAsoValue(objId, fldId);
        if (!asoValue.HasValue)
            throw new Exception("what should happen here?");

        return new T { _transaction = transaction, _objId = asoValue.Value };
    }

    public static void SetAssoc(Transaction transaction, Guid objIdA, Guid fldIdA, Guid objIdB, Guid fldIdB)
    {
        if (objIdB == Guid.Empty)
        {
            transaction.RemoveAllAso(objIdA, fldIdA);
        }
        else
        {
            transaction.CreateAso(objIdA, fldIdA, objIdB, fldIdB);
        }
    }
}

public struct AssoCollectionEnumerator<T> : IEnumerator<T> where T : struct, ITransactionObject
{
    private readonly Transaction _transaction;
    private AsoFldEnumerator _asoFldEnumerator;
    private T _current;

    public AssoCollectionEnumerator(Transaction transaction, AsoFldEnumerator asoFldEnumerator)
    {
        _transaction = transaction;
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
            _current._transaction = _transaction;
            _current._objId = _asoFldEnumerator.Current.ObjId;
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
    public Transaction _transaction;

    public AssocCollection(Transaction transaction, Guid objId, Guid fldId, Guid otherFld)
    {
        _transaction = transaction;
        _objId = objId;
        _fldId = fldId;
        _otherFld = otherFld;
    }

    public AssoCollectionEnumerator<T> GetEnumerator()
    {
        return new AssoCollectionEnumerator<T>(_transaction, _transaction.EnumerateAso(_objId, _fldId).GetEnumerator());
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
        _transaction.CreateAso(_objId, _fldId, item._objId, _otherFld);
    }

    public void Clear()
    {
        _transaction.RemoveAllAso(_objId, _fldId);
    }

    public bool Contains(T item)
    {
        foreach (var otherObj in _transaction.EnumerateAso(_objId, _fldId))
        {
            if (otherObj.ObjId == item._objId)
                return true;
        }

        return false;
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        throw new NotImplementedException();
    }

    public bool Remove(T item)
    {
        return _transaction.RemoveAso(_objId, _fldId, item._objId, _otherFld);
    }

    public int Count => _transaction.GetAsoCount(_objId, _fldId);
    public bool IsReadOnly => false;

}

