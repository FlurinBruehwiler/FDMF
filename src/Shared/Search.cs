using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LightningDB;
using Shared.Database;

namespace Shared;

public struct FieldCriterion
{
    public Guid FieldId;
    public string Value;
}

public static class Searcher
{
    public static IEnumerable<T> Search<T>(DbSession dbSession, params FieldCriterion[] criteria) where T : ITransactionObject, new()
    {
        Guid[]? results = null;

        foreach (var criterion in criteria)
        {
            //todo assert that the criterion is valid (that the fieldId is part of T)

            var r = ExecuteStringSearch(dbSession.Environment, dbSession.Store.ReadTransaction, criterion.FieldId, criterion.Value);
            if (results is null)
            {
                results = r;
            }
            else
            {
                results = results.Intersect(r).ToArray();
            }
        }

        if (results == null)
            yield break;

        foreach (var result in results)
        {
            yield return new T
            {
                ObjId = result,
                DbSession = dbSession
            };
        }
    }


    public static Guid[] ExecuteStringSearch(Environment environment, LightningTransaction transaction, Guid fldId, ReadOnlySpan<char> searchString)
    {
        using var cursor = transaction.CreateCursor(environment.SearchIndex);

        var stringAsBytes = MemoryMarshal.Cast<char, byte>(searchString);

        var set = new HashSet<Guid>();

        var prefixForwardKey = ConstructIndexKey(fldId, stringAsBytes);
        Collect(prefixForwardKey);

        var prefixBackwardKey = ConstructIndexKey(fldId, stringAsBytes, reverse: true);
        Collect(prefixBackwardKey);

        return set.ToArray();

        void Collect(byte[] prefixKey)
        {
            if (cursor.SetRange(prefixKey) == MDBResultCode.Success)
            {
                do
                {
                    var (_, key, value) = cursor.GetCurrent();

                    if (!key.AsSpan().StartsWith(prefixKey))
                        break;

                    set.Add(MemoryMarshal.Read<Guid>(value.AsSpan()));

                } while (cursor.Next().resultCode == MDBResultCode.Success);
            }
        }
    }

    public static void BuildSearchIndex(Environment environment)
    {
        var indexDb = environment.SearchIndex;

        using var transaction = environment.LightningEnvironment.BeginTransaction();

        using var cursor = transaction.CreateCursor(environment.ObjectDb);
        if (cursor.SetRange([0]) == MDBResultCode.Success)
        {
            do
            {
                var (_, key, value) = cursor.GetCurrent();

                if (value.AsSpan()[0] == (byte)ValueTyp.Val)
                {
                    var dataValue = value.AsSpan().Slice(1); //ignore tag

                    var objId = MemoryMarshal.Read<Guid>(key.AsSpan().Slice(0, 16));
                    var fldId = MemoryMarshal.Read<Guid>(key.AsSpan().Slice(16));
                    if (environment.FldsToIndex.Contains(fldId))
                    {
                        Insert(objId, fldId, dataValue, transaction, indexDb);

                        //todo in the middle of the string...
                        //index for different data types
                    }
                }

            } while (cursor.Next().resultCode == MDBResultCode.Success);
        }

        transaction.Commit();
    }

    private static byte[] ConstructIndexKey(Guid fieldId, ReadOnlySpan<byte> value, bool reverse = false)
    {
        var fldIdSpan = fieldId.AsSpan();

        var forwardIndexKey = new byte[fldIdSpan.Length + value.Length];
        fldIdSpan.CopyTo(forwardIndexKey);

        if (reverse)
        {
            MemoryMarshal.Cast<byte, char>(value).CopyToReverse(MemoryMarshal.Cast<byte, char>(forwardIndexKey.AsSpan(16)));
        }
        else
        {
            value.CopyTo(forwardIndexKey.AsSpan(16));
        }

        return forwardIndexKey;
    }

    /// <summary>
    /// Updates the SearchIndex, needs to be called before the changeSet is commited to the baseSet, as we need to know the old value.
    /// </summary>
    public static void UpdateSearchIndex(Environment environment, LightningTransaction txn, BPlusTree changeSet)
    {
        using var indexCursor = txn.CreateCursor(environment.SearchIndex);

        var changeCursor = changeSet.CreateCursor();
        if (changeCursor.SetRange([0]) == ResultCode.Success)
        {
            do
            {
                var (_, key, value) = changeCursor.GetCurrent();

                if(key.AsSpan().Length != 32)
                    continue;

                var objId = MemoryMarshal.Read<Guid>(key.AsSpan());
                var fldId = MemoryMarshal.Read<Guid>(key.AsSpan(16));

                if (environment.FldsToIndex.Contains(fldId))
                {
                    var (r, _, oldValue) = txn.Get(environment.ObjectDb, key);

                    if (r == MDBResultCode.Success) //the value already existed, we remove it
                    {
                        var indexKey = ConstructIndexKey(fldId, oldValue.AsSpan().Slice(1)); //ignore tag

                        if (indexCursor.GetBoth(indexKey, objId.AsSpan()) == MDBResultCode.Success)
                        {
                            indexCursor.Delete();

                            var indexKey2 = ConstructIndexKey(fldId, oldValue.AsSpan().Slice(1), reverse: true); //ignore tag
                            indexCursor.GetBoth(indexKey2, objId.AsSpan());
                            indexCursor.Delete();
                        }
                    }

                    if (value[0] == (byte)ValueFlag.AddModify)
                    {
                        Insert(objId, fldId, value.AsSpan(2), txn, environment.SearchIndex);
                    }
                }

            } while (changeCursor.Next().resultCode == ResultCode.Success);
        }
    }

    private static void Insert(Guid objId, Guid fldId, ReadOnlySpan<byte> dataValue, LightningTransaction transaction, LightningDatabase indexDb)
    {
        var forwardIndexKey = ConstructIndexKey(fldId, dataValue);
        transaction.Put(indexDb, forwardIndexKey.AsSpan(), objId.AsSpan()); //forward

        var backwardIndexKey = ConstructIndexKey(fldId, dataValue, reverse: true);
        transaction.Put(indexDb, backwardIndexKey.AsSpan(), objId.AsSpan()); //forward
    }
}