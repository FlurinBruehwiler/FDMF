using System.Runtime.InteropServices;
using LightningDB;
using Shared.Database;

namespace Shared;

public struct StringCriterion
{
    public Guid FieldId;
    public string Value;
    public MatchType Type;

    public enum MatchType
    {
        Substring = 0,
        Exact,
        Prefix,
        Postfix,
    }
}

public static class Searcher
{
    public static IEnumerable<T> Search<T>(DbSession dbSession, params StringCriterion[] criteria) where T : ITransactionObject, new()
    {
        Guid[]? results = null;

        foreach (var criterion in criteria)
        {
            //todo assert that the criterion is valid (that the fieldId is part of T)

            var r = ExecuteStringSearch(dbSession.Environment, dbSession.Store.ReadTransaction, criterion);
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

    public static Guid[] ExecuteStringSearch(Environment environment, LightningTransaction transaction, StringCriterion criterion)
    {
        using var cursor = transaction.CreateCursor(environment.SearchIndex);

        var strValue = Normalize(criterion.Value);

        var set = new HashSet<Guid>();

        if (criterion.Type == StringCriterion.MatchType.Exact)
        {
            var exactKey = ConstructIndexKey(IndexFlag.Normal, criterion.FieldId, strValue);
            Collect(exactKey, exact: true);
        }
        else if (criterion.Type == StringCriterion.MatchType.Prefix)
        {
            var prefixForwardKey = ConstructIndexKey(IndexFlag.Normal, criterion.FieldId, strValue);
            Collect(prefixForwardKey, exact: false);
        }
        else if (criterion.Type == StringCriterion.MatchType.Postfix)
        {
            var prefixBackwardKey = ConstructIndexKey(IndexFlag.Reverse, criterion.FieldId, strValue);
            Collect(prefixBackwardKey, exact: false);
        }
        else if (criterion.Type == StringCriterion.MatchType.Substring)
        {
            //ngram search
            if (strValue.Length >= 3)
            {
                HashSet<Guid> lastRound = [];
                HashSet<Guid> thisRound = [];

                for (int i = 0; i < strValue.Length - 2; i++)
                {
                    var ngramKey = ConstructIndexKey(IndexFlag.NGram, criterion.FieldId, strValue.AsSpan().Slice(i, 3));

                    if (cursor.SetRange(ngramKey) == MDBResultCode.Success)
                    {
                        do
                        {
                            var (_, key, value) = cursor.GetCurrent();

                            if (!key.AsSpan().StartsWith(ngramKey))
                                break;

                            var guid = MemoryMarshal.Read<Guid>(value.AsSpan());

                            if (i == 0 || lastRound.Contains(guid))
                            {
                                thisRound.Add(guid);
                            }
                        } while (cursor.Next().resultCode == MDBResultCode.Success);
                    }

                    if (thisRound.Count == 0)
                        break;

                    (thisRound, lastRound) = (lastRound, thisRound);
                    thisRound.Clear();
                }

                foreach (var guid in lastRound)
                {
                    set.Add(guid);
                }
            }
        }

        return set.ToArray();

        void Collect(byte[] prefixKey, bool exact)
        {
            if (cursor.SetRange(prefixKey) == MDBResultCode.Success)
            {
                do
                {
                    var (_, key, value) = cursor.GetCurrent();

                    if(exact && !key.AsSpan().SequenceEqual(prefixKey))
                        break;

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
                        Insert(objId, fldId, MemoryMarshal.Cast<byte, char>(dataValue), transaction, indexDb);

                        //index for different data types
                    }
                }
            } while (cursor.Next().resultCode == MDBResultCode.Success);
        }

        transaction.Commit();
    }

    private static byte[] ConstructIndexKey(IndexFlag indexFlag, Guid fieldId, ReadOnlySpan<char> stringValue)
    {
        var value = MemoryMarshal.Cast<char, byte>(stringValue);

        var fldIdSpan = fieldId.AsSpan();

        var forwardIndexKey = new byte[1 + fldIdSpan.Length + value.Length];
        forwardIndexKey[0] = (byte)indexFlag;
        fldIdSpan.CopyTo(forwardIndexKey.AsSpan(1));

        if (indexFlag == IndexFlag.Reverse)
        {
            MemoryMarshal.Cast<byte, char>(value).CopyToReverse(MemoryMarshal.Cast<byte, char>(forwardIndexKey.AsSpan(1 + 16)));
        }
        else
        {
            value.CopyTo(forwardIndexKey.AsSpan(1 + 16));
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

                if (key.AsSpan().Length != 32)
                    continue;

                var objId = MemoryMarshal.Read<Guid>(key.AsSpan());
                var fldId = MemoryMarshal.Read<Guid>(key.AsSpan(16));

                if (environment.FldsToIndex.Contains(fldId))
                {
                    var (r, _, oldValue) = txn.Get(environment.ObjectDb, key);

                    if (r == MDBResultCode.Success) //the value already existed, we remove it
                    {
                        var oldValueSpan = Normalize(MemoryMarshal.Cast<byte, char>(oldValue.AsSpan().Slice(1))).AsSpan();
                        var indexKey = ConstructIndexKey(IndexFlag.Normal, fldId, oldValueSpan); //ignore tag

                        if (indexCursor.GetBoth(indexKey, objId.AsSpan()) == MDBResultCode.Success)
                        {
                            indexCursor.Delete();

                            var indexKey2 = ConstructIndexKey(IndexFlag.Reverse, fldId, oldValueSpan); //ignore tag
                            indexCursor.GetBoth(indexKey2, objId.AsSpan());
                            indexCursor.Delete();

                            if (oldValueSpan.Length >= 3)
                            {
                                for (int i = 0; i < oldValueSpan.Length - 2; i++)
                                {
                                    var ngramIndexKey = ConstructIndexKey(IndexFlag.NGram, fldId, oldValueSpan.Slice(i, 3));
                                    indexCursor.GetBoth(ngramIndexKey, objId.AsSpan());
                                    indexCursor.Delete();
                                }
                            }
                        }
                    }

                    if (value[0] == (byte)ValueFlag.AddModify)
                    {
                        Insert(objId, fldId, MemoryMarshal.Cast<byte, char>(value.AsSpan(2)), txn, environment.SearchIndex);
                    }
                }
            } while (changeCursor.Next().resultCode == ResultCode.Success);
        }
    }

    //key: [flag][fldid][value]:[obj]

    private static void Insert(Guid objId, Guid fldId, ReadOnlySpan<char> str, LightningTransaction transaction, LightningDatabase indexDb)
    {
        str = Normalize(str);

        var forwardIndexKey = ConstructIndexKey(IndexFlag.Normal, fldId, str);
        transaction.Put(indexDb, forwardIndexKey.AsSpan(), objId.AsSpan()); //forward

        var backwardIndexKey = ConstructIndexKey(IndexFlag.Reverse, fldId, str);
        transaction.Put(indexDb, backwardIndexKey.AsSpan(), objId.AsSpan()); //forward

        if (str.Length >= 3)
        {
            for (int i = 0; i < str.Length - 2; i++)
            {
                var ngramIndexKey = ConstructIndexKey(IndexFlag.NGram, fldId, str.Slice(i, 3));
                transaction.Put(indexDb, ngramIndexKey.AsSpan(), objId.AsSpan()); //forward
            }
        }
    }

    private static string Normalize(ReadOnlySpan<char> input)
    {
        return input.ToString().ToLower();//todo don't allocate two strings!!!
    }
}

public enum IndexFlag : byte
{
    Normal,
    Reverse,
    NGram
}