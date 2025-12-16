using System.Diagnostics;
using System.Runtime.InteropServices;
using LightningDB;
using Shared.Database;

namespace Shared;

public struct SearchCriterion
{
    public CriterionType Type;
    public StringCriterion String;

    //todo overlap the following three fields so that the struct is smaller
    public LongCriterion Long;
    public DecimalCriterion Decimal;
    public DateTimeCriterion DateTime;

    public enum CriterionType
    {
        String,
        Long,
        Decimal,
        DateTime
    }

    public struct LongCriterion
    {
        public Guid FieldId;
        public long From;
        public long To;
    }

    public struct DecimalCriterion
    {
        public Guid FieldId;
        public decimal From;
        public decimal To;
    }

    public struct DateTimeCriterion
    {
        public Guid FieldId;
        public DateTime From;
        public DateTime To;
    }

    public struct StringCriterion
    {
        public Guid FieldId;
        public string Value;
        public MatchType Type;

        public enum MatchType
        {
            Substring = 0, //default
            Exact,
            Prefix,
            Postfix,
        }
    }
}

public static class Searcher
{
    public static IEnumerable<T> Search<T>(DbSession dbSession, params SearchCriterion[] criteria) where T : ITransactionObject, new()
    {
        Guid[]? results = null;

        foreach (var criterion in criteria)
        {
            //todo assert that the criterion is valid (that the fieldId is part of T)

            var r = criterion.Type switch
            {
                SearchCriterion.CriterionType.String => ExecuteStringSearch(dbSession.Environment, dbSession.Store.ReadTransaction, criterion.String),
                SearchCriterion.CriterionType.Long => ExecuteNonStringSearch(dbSession.Environment, dbSession.Store.ReadTransaction, criterion.Long.FieldId, criterion.Long.From, criterion.Long.To),
                SearchCriterion.CriterionType.Decimal => ExecuteNonStringSearch(dbSession.Environment, dbSession.Store.ReadTransaction, criterion.Decimal.FieldId, criterion.Decimal.From, criterion.Decimal.To),
                SearchCriterion.CriterionType.DateTime => ExecuteNonStringSearch(dbSession.Environment, dbSession.Store.ReadTransaction, criterion.DateTime.FieldId, criterion.DateTime.From, criterion.DateTime.To),
                _ => throw new ArgumentOutOfRangeException()
            };

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

    public static Guid[] ExecuteNonStringSearch<T>(Environment environment, LightningTransaction transaction, Guid fieldId, T from, T to) where T : unmanaged
    {
        using var cursor = transaction.CreateCursor(environment.NonStringSearchIndex);

        Span<byte> minKey = stackalloc byte[GetNonStringKeySize<DateTime>()];
        ConstructNonStringIndexKey(CustomIndexComparer.Comparison.DateTime, fieldId, from, minKey);

        Span<byte> maxKey = stackalloc byte[GetNonStringKeySize<DateTime>()];
        ConstructNonStringIndexKey(CustomIndexComparer.Comparison.DateTime, fieldId, to, minKey);

        var set = new HashSet<Guid>();

        if (cursor.SetRange(minKey) == MDBResultCode.Success)
        {
            do
            {
                var (_, key, value) = cursor.GetCurrent();

                if (CustomIndexComparer.CompareStatic(maxKey, key.AsSpan()) < 0)
                    break;

                set.Add(MemoryMarshal.Read<Guid>(value.AsSpan()));
            } while (cursor.Next().resultCode == MDBResultCode.Success);
        }

        return set.ToArray();
    }

    public static Guid[] ExecuteStringSearch(Environment environment, LightningTransaction transaction, SearchCriterion.StringCriterion criterion)
    {
        using var cursor = transaction.CreateCursor(environment.StringSearchIndex);

        var strValue = Normalize(criterion.Value);

        var set = new HashSet<Guid>();

        if (criterion.Type == SearchCriterion.StringCriterion.MatchType.Exact)
        {
            var exactKey = ConstructStringIndexKey(IndexFlag.Normal, criterion.FieldId, strValue);
            Collect(exactKey, exact: true);
        }
        else if (criterion.Type == SearchCriterion.StringCriterion.MatchType.Prefix)
        {
            var prefixForwardKey = ConstructStringIndexKey(IndexFlag.Normal, criterion.FieldId, strValue);
            Collect(prefixForwardKey, exact: false);
        }
        else if (criterion.Type == SearchCriterion.StringCriterion.MatchType.Postfix)
        {
            var prefixBackwardKey = ConstructStringIndexKey(IndexFlag.Reverse, criterion.FieldId, strValue);
            Collect(prefixBackwardKey, exact: false);
        }
        else if (criterion.Type == SearchCriterion.StringCriterion.MatchType.Substring)
        {
            //ngram search
            if (strValue.Length >= 3)
            {
                HashSet<Guid> lastRound = [];
                HashSet<Guid> thisRound = [];

                for (int i = 0; i < strValue.Length - 2; i++)
                {
                    var ngramKey = ConstructStringIndexKey(IndexFlag.NGram, criterion.FieldId, strValue.AsSpan().Slice(i, 3));

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
        var indexDb = environment.StringSearchIndex;

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

                        //todo index for different data types
                    }
                }
            } while (cursor.Next().resultCode == MDBResultCode.Success);
        }

        transaction.Commit();
    }

    private static unsafe int GetNonStringKeySize<T>() where T : unmanaged
    {
        return 1 + 16 + sizeof(T);
    }

    private static void ConstructNonStringIndexKey<T>(CustomIndexComparer.Comparison comparison, Guid fieldId, T data, Span<byte> destination) where T : unmanaged
    {
        Debug.Assert(GetNonStringKeySize<T>() == destination.Length);

        destination[0] = (byte)comparison;
        fieldId.AsSpan().CopyTo(destination.Slice(1));

        MemoryMarshal.Write<T>(destination.Slice(1 + 16), data);
    }

    private static byte[] ConstructStringIndexKey(IndexFlag indexFlag, Guid fieldId, ReadOnlySpan<char> stringValue)
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
        using var indexCursor = txn.CreateCursor(environment.StringSearchIndex);

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
                        var indexKey = ConstructStringIndexKey(IndexFlag.Normal, fldId, oldValueSpan); //ignore tag

                        if (indexCursor.GetBoth(indexKey, objId.AsSpan()) == MDBResultCode.Success)
                        {
                            indexCursor.Delete();

                            var indexKey2 = ConstructStringIndexKey(IndexFlag.Reverse, fldId, oldValueSpan); //ignore tag
                            indexCursor.GetBoth(indexKey2, objId.AsSpan());
                            indexCursor.Delete();

                            if (oldValueSpan.Length >= 3)
                            {
                                for (int i = 0; i < oldValueSpan.Length - 2; i++)
                                {
                                    var ngramIndexKey = ConstructStringIndexKey(IndexFlag.NGram, fldId, oldValueSpan.Slice(i, 3));
                                    indexCursor.GetBoth(ngramIndexKey, objId.AsSpan());
                                    indexCursor.Delete();
                                }
                            }
                        }
                    }

                    if (value[0] == (byte)ValueFlag.AddModify)
                    {
                        Insert(objId, fldId, MemoryMarshal.Cast<byte, char>(value.AsSpan(2)), txn, environment.StringSearchIndex);
                    }
                }
            } while (changeCursor.Next().resultCode == ResultCode.Success);
        }
    }

    //key: [flag][fldid][value]:[obj]

    private static void Insert(Guid objId, Guid fldId, ReadOnlySpan<char> str, LightningTransaction transaction, LightningDatabase indexDb)
    {
        str = Normalize(str);

        var forwardIndexKey = ConstructStringIndexKey(IndexFlag.Normal, fldId, str);
        transaction.Put(indexDb, forwardIndexKey.AsSpan(), objId.AsSpan()); //forward

        var backwardIndexKey = ConstructStringIndexKey(IndexFlag.Reverse, fldId, str);
        transaction.Put(indexDb, backwardIndexKey.AsSpan(), objId.AsSpan()); //forward

        if (str.Length >= 3)
        {
            for (int i = 0; i < str.Length - 2; i++)
            {
                var ngramIndexKey = ConstructStringIndexKey(IndexFlag.NGram, fldId, str.Slice(i, 3));
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