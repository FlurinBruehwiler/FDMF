using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using LightningDB;

namespace Shared.Database;

//key: [flag][fldid][value]:[obj]

//todo at some point we also want the functionality to search the non-commited data (these wouldn't need an index)

//todo the following features need to be implemented
// [x] implement search of non indexed values fields, ok the problem with this is, that with the current architecture,
//          objects are not grouped by type, we can still implement non-indexed search, but it is not very efficient.
//          We could try to either group by type, or even more extreme, group by field, which would lead to an SOA,
//          which would be very efficient when searching. The problem its, that this would make other things more complex,
//          such as deleting objs, so for now we could just stick with the current layout. Another possibility,
//          is to make the type part of the objId, so that start of any ObjId, is actually its type.
// [ ] implement ranking
// [ ] implement partial results
// [ ] implement result info (for example in a substring search, we want to see the part of the substring that matched
// [ ] implement profiling (we want to see what searches are slow, so the user can add indexes to these fields
// [ ] implement more complex operators (not only AND, but also OR)
// [ ] implement sub queries
// [ ] implement assoc null/not null search (without indexing for now..)
// [ ] implement better substring/fuzzy search
// [ ] improve the performance (reducing allocations)
// [x] implement search by type (indexed)

/// <summary>
/// Method for Searching the Database and maintaining indexes.
/// </summary>
public static class Searcher
{
    public static IEnumerable<T> Search<T>(DbSession dbSession, ISearchCriterion? searchQuery = null) where T : ITransactionObject, new()
    {
        foreach (var result in Search(dbSession, new SearchQuery
                 {
                     TypId = T.TypId,
                     SearchCriterion = searchQuery
                 }))
        {
            yield return new T
            {
                ObjId = result,
                DbSession = dbSession
            };
        }
    }

    public static IEnumerable<Guid> Search(DbSession dbSession, SearchQuery searchQuery)
    {
        var result = new List<Guid>();

        SearchInternal(dbSession, searchQuery, x =>
        {
            result.Add(x);
            return true;
        });

        return result;
    }

    private static void SearchInternal(DbSession dbSession, ISearchCriterion criterion, Func<Guid, bool> addResult)
    {
        switch (criterion)
        {
            case IdCriterion idCriterion:
                if (dbSession.GetTypId(idCriterion.Guid) != Guid.Empty)
                {
                    addResult(idCriterion.Guid);
                }
                break;
            case SearchQuery searchQuery:
                if (searchQuery.SearchCriterion == null)
                {
                    ExecuteTypeSearch(dbSession.Environment, dbSession.Store.ReadTransaction, searchQuery.TypId, addResult);
                }
                else
                {
                    SearchInternal(dbSession, searchQuery.SearchCriterion, addResult);
                }

                break;
            case OrCriterion orCriterion:
                foreach (var crit in orCriterion.OrCombinations)
                {
                    SearchInternal(dbSession, crit, addResult);
                }

                break;
            case AndCriterion andCriterion:
                HashSet<Guid> lastRound = [];
                HashSet<Guid> thisRound = [];

                int i = 0;

                Func<Guid, bool> resultAdd = (guid) =>
                {
                    if (i == 0 || lastRound.Contains(guid))
                    {
                        thisRound.Add(guid);
                    }

                    return true;
                };

                foreach (var crit in andCriterion.AndCombinations)
                {
                    SearchInternal(dbSession, crit, resultAdd);

                    thisRound.Clear();

                    i++;
                    (lastRound, thisRound) = (thisRound, lastRound);
                }

                foreach (var guid in lastRound)
                {
                    addResult(guid);
                }

                break;
            case AssocCriterion assocCriterion:
                ExecuteAssocSearch(dbSession, assocCriterion, addResult);
                break;
            case DateTimeCriterion dateTimeCriterion:
                ExecuteNonStringSearch(dbSession.Environment, dbSession.Store.ReadTransaction, addResult, CustomIndexComparer.Comparison.SignedLong, dateTimeCriterion.FieldId, dateTimeCriterion.From, dateTimeCriterion.To);
                break;
            case DecimalCriterion decimalCriterion:
                ExecuteNonStringSearch(dbSession.Environment, dbSession.Store.ReadTransaction, addResult, CustomIndexComparer.Comparison.SignedLong, decimalCriterion.FieldId, decimalCriterion.From, decimalCriterion.To);
                break;
            case LongCriterion longCriterion:
                ExecuteNonStringSearch(dbSession.Environment, dbSession.Store.ReadTransaction, addResult, CustomIndexComparer.Comparison.SignedLong, longCriterion.FieldId, longCriterion.From, longCriterion.To);
                break;
            case StringCriterion stringCriterion:
                ExecuteStringSearch(dbSession.Environment, dbSession.Store.ReadTransaction, stringCriterion, addResult);
                break;
        }
    }

    public static void BuildIndex(Environment environment)
    {
        using var txn = environment.LightningEnvironment.BeginTransaction();

        using var cursor = txn.CreateCursor(environment.ObjectDb);
        if (cursor.First().resultCode == MDBResultCode.Success)
        {
            do
            {
                var (_, key, value) = cursor.GetCurrent();

                if (value.AsSpan()[0] == (byte)ValueTyp.Val)
                {
                    var dataValue = value.AsSpan().Slice(1); //ignore tag

                    var objId = MemoryMarshal.Read<Guid>(key.AsSpan().Slice(0, 16));
                    var fldId = MemoryMarshal.Read<Guid>(key.AsSpan().Slice(16));
                    if (environment.Model.FieldsById.TryGetValue(fldId, out var fld) && fld.IsIndexed)
                    {
                        InsertIndex(fld.DataType, objId, fldId, dataValue, txn, environment);
                    }
                }
                else if (value.AsSpan()[0] == (byte)ValueTyp.Obj)
                {
                    var objId = MemoryMarshal.Read<Guid>(key.AsSpan());
                    var typId = MemoryMarshal.Read<Guid>(value.AsSpan().Slice(1));

                    InsertTypeIndex(environment, typId, txn, objId);
                }
            } while (cursor.Next().resultCode == MDBResultCode.Success);
        }

        txn.Commit();
    }

    /// <summary>
    /// Updates the SearchIndex, needs to be called before the changeSet is commited to the baseSet, as we need to know the old value.
    /// </summary>
    public static void UpdateSearchIndex(Environment environment, LightningTransaction txn, BPlusTree changeSet)
    {
        var changeCursor = changeSet.CreateCursor();
        using var baseCursor = txn.CreateCursor(environment.ObjectDb);

        if (changeCursor.SetRange([0]) == ResultCode.Success)
        {
            do
            {
                var (_, key, value) = changeCursor.GetCurrent();

                if (key.AsSpan().Length < 16)
                    continue;

                var objId = MemoryMarshal.Read<Guid>(key.AsSpan());

                if (key.AsSpan().Length == 16) //obj
                {
                    txn.Delete(environment.ObjectDb, objId.AsSpan());

                    var typId = MemoryMarshal.Read<Guid>(value.AsSpan().Slice(2));

                    if (value[0] == (byte)ValueFlag.AddModify)
                    {
                        InsertTypeIndex(environment, typId, txn, objId);
                    }
                }
                else if (key.AsSpan().Length == 32) //val
                {
                    var fldId = MemoryMarshal.Read<Guid>(key.AsSpan(16));

                    if (environment.Model.FieldsById.TryGetValue(fldId, out var fieldDefinition) && fieldDefinition.IsIndexed)
                    {
                        var (r, _, v) = txn.Get(environment.ObjectDb, key);

                        if (r == MDBResultCode.Success) //the value already existed, we remove it
                        {
                            var oldValue = v.AsSpan().Slice(1);
                            switch (fieldDefinition.DataType)
                            {
                                case FieldDataType.String:
                                    var oldValueSpan = Normalize(MemoryMarshal.Cast<byte, char>(oldValue)).AsSpan();

                                    var indexKey = ConstructStringIndexKey(IndexFlag.Normal, fldId, oldValueSpan); //ignore tag
                                    txn.Delete(environment.StringSearchIndex, indexKey, objId.AsSpan());

                                    var indexKey2 = ConstructStringIndexKey(IndexFlag.Reverse, fldId, oldValueSpan); //ignore tag
                                    txn.Delete(environment.StringSearchIndex, indexKey2, objId.AsSpan());

                                    if (oldValueSpan.Length >= 3)
                                    {
                                        for (int i = 0; i < oldValueSpan.Length - 2; i++)
                                        {
                                            var ngramIndexKey = ConstructStringIndexKey(IndexFlag.NGram, fldId, oldValueSpan.Slice(i, 3));
                                            txn.Delete(environment.StringSearchIndex, ngramIndexKey, objId.AsSpan());
                                        }
                                    }

                                    break;
                                case FieldDataType.DateTime:
                                    objId = RemoveNonStringIndexValue<DateTime>(oldValue, CustomIndexComparer.Comparison.DateTime, fldId, objId, txn, environment.NonStringSearchIndex);
                                    break;
                                case FieldDataType.Integer:
                                    objId = RemoveNonStringIndexValue<long>(oldValue, CustomIndexComparer.Comparison.SignedLong, fldId, objId, txn, environment.NonStringSearchIndex);
                                    break;
                                case FieldDataType.Decimal:
                                    objId = RemoveNonStringIndexValue<decimal>(oldValue, CustomIndexComparer.Comparison.Decimal, fldId, objId, txn, environment.NonStringSearchIndex);
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }

                        if (value[0] == (byte)ValueFlag.AddModify)
                        {
                            var val = value.AsSpan(2);
                            InsertIndex(fieldDefinition.DataType, objId, fldId, val, txn, environment);
                        }
                    }
                }
            } while (changeCursor.Next().resultCode == ResultCode.Success);
        }
    }

    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    private static void ExecuteAssocSearch(DbSession dbSession, AssocCriterion criterion, Func<Guid, bool> addResult)
    {
        Environment env = dbSession.Environment;
        LightningTransaction transaction = dbSession.Store.ReadTransaction;

        var fld = env.Model.AsoFieldsById[criterion.FieldId];

        using var cursor = transaction.CreateCursor(env.ObjectDb);

        switch (criterion.Type)
        {
            case AssocCriterion.AssocCriterionType.Subquery:
                if (criterion.SearchCriterion != null)
                {
                    //todo we need to validate of this criterion is even valid, i.e. the object actually has the fields
                    SearchInternal(dbSession, criterion.SearchCriterion, objId =>
                    {
                        Span<byte> key = stackalloc byte[2 * 16];
                        MemoryMarshal.Write(key, objId);
                        MemoryMarshal.Write(key.Slice(16), fld.OtherReferenceFielGuid);

                        if (cursor.SetRange(key) == MDBResultCode.Success)
                        {
                            do
                            {
                                var (_, k, _) = cursor.GetCurrent();

                                if (k.AsSpan().Length != 64 || !key.SequenceEqual(k.AsSpan().Slice(0, 2 * 16)))
                                    break;

                                addResult(MemoryMarshal.Read<Guid>(k.AsSpan().Slice(2 * 16, 16)));
                            } while (cursor.Next().resultCode == MDBResultCode.Success);
                        }

                        return true;
                    });
                }
                else
                {
                    Console.WriteLine("A SearchCriterion needs to be specified if the search type is Subquery");
                }

                break;
            case AssocCriterion.AssocCriterionType.MatchGuid:

                Span<byte> key = stackalloc byte[2 * 16];
                MemoryMarshal.Write(key, criterion.ObjId);
                MemoryMarshal.Write(key.Slice(16), fld.OtherReferenceFielGuid);

                if (cursor.SetRange(key) == MDBResultCode.Success)
                {
                    do
                    {
                        var (_, k, _) = cursor.GetCurrent();

                        if (k.AsSpan().Length != 64 || !key.SequenceEqual(k.AsSpan().Slice(0, 2 * 16)))
                            break;

                        addResult(MemoryMarshal.Read<Guid>(k.AsSpan().Slice(2 * 16, 16)));
                    } while (cursor.Next().resultCode == MDBResultCode.Success);
                }

                break;
            case AssocCriterion.AssocCriterionType.Null:
                //we have decided that for now we won't index these two cases (null and notnull) as it would be quite expensive,
                //and it is not clear if it is worth it.
                break;
            case AssocCriterion.AssocCriterionType.NotNull:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        throw new NotImplementedException();
    }

    private static void ExecuteNonStringSearch<T>(Environment environment, LightningTransaction transaction, Func<Guid, bool> addResult, CustomIndexComparer.Comparison comparison, Guid fieldId, T from, T to) where T : unmanaged, IComparable<T>
    {
        var fld = environment.Model.FieldsById.GetValueOrDefault(fieldId);

        if (fld == null)
        {
            Console.WriteLine($"There isn't a Field with the ID {fieldId}"); //todo proper logging system
            return;
        }

        if (!fld.IsIndexed)
        {
            ExecuteTypeSearch(environment, transaction, fld.OwningEntity.Id, (objId) =>
            {
                var val = GetFldValue<byte>(environment, transaction, objId, fieldId);

                if (CustomIndexComparer.CompareGeneric<T>(val, from.AsSpan()) >= 0)
                {
                    if (CustomIndexComparer.CompareGeneric<T>(val, to.AsSpan()) <= 0)
                    {
                        addResult(objId);
                    }
                }

                return true;
            });

            return;
        }

        using var cursor = transaction.CreateCursor(environment.NonStringSearchIndex);

        Span<byte> minKey = stackalloc byte[GetNonStringKeySize<T>()];
        ConstructNonStringIndexKey(comparison, fieldId, from, minKey);

        Span<byte> maxKey = stackalloc byte[GetNonStringKeySize<T>()];
        ConstructNonStringIndexKey(comparison, fieldId, to, maxKey);


        if (cursor.SetRange(minKey) == MDBResultCode.Success)
        {
            do
            {
                var (_, key, value) = cursor.GetCurrent();

                if (CustomIndexComparer.CompareStatic(maxKey, key.AsSpan()) < 0)
                    break;

                addResult(MemoryMarshal.Read<Guid>(value.AsSpan()));
            } while (cursor.Next().resultCode == MDBResultCode.Success);
        }
    }

    private static void ExecuteTypeSearch(Environment environment, LightningTransaction transaction, Guid typId, Func<Guid, bool> addResult)
    {
        using var cursor = transaction.CreateCursor(environment.NonStringSearchIndex);

        Span<byte> dest = stackalloc byte[1 + 16];
        dest[0] = (byte)CustomIndexComparer.Comparison.Type;
        typId.AsSpan().CopyTo(dest.Slice(1));

        if (cursor.SetKey(dest).resultCode == MDBResultCode.Success)
        {
            do
            {
                var (_, _, value) = cursor.GetCurrent();

                var guid = MemoryMarshal.Read<Guid>(value.AsSpan());

                addResult(guid);
            } while (cursor.NextDuplicate().resultCode == MDBResultCode.Success);
        }
    }

    private static ReadOnlySpan<T> GetFldValue<T>(Environment environment, LightningTransaction transaction, Guid objId, Guid fldId) where T : unmanaged
    {
        Span<byte> keyBuf = stackalloc byte[2 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0 * 16, 16), objId);
        MemoryMarshal.Write(keyBuf.Slice(1 * 16, 16), fldId);

        var (r, _, v) = transaction.Get(environment.ObjectDb, keyBuf);
        if (r == MDBResultCode.Success)
        {
            return MemoryMarshal.Cast<byte, T>(v.AsSpan().Slice(1));
        }

        return ReadOnlySpan<T>.Empty;
    }

    private static void ExecuteStringSearch(Environment environment, LightningTransaction transaction, StringCriterion criterion, Func<Guid, bool> addResult)
    {
        var fld = environment.Model.FieldsById[criterion.FieldId];

        if (!fld.IsIndexed)
        {
            ExecuteTypeSearch(environment, transaction, fld.OwningEntity.Id, (objId) =>
            {
                switch (criterion.Type)
                {
                    case StringCriterion.MatchType.Substring:
                    {
                        var val = GetFldValue<char>(environment, transaction, objId, criterion.FieldId);

                        if (val.Contains(criterion.Value.AsSpan(), StringComparison.OrdinalIgnoreCase))
                            addResult(objId);
                        break;
                    }
                    case StringCriterion.MatchType.Exact:
                    {
                        var val = GetFldValue<char>(environment, transaction, objId, criterion.FieldId);

                        if (val.Equals(criterion.Value.AsSpan(), StringComparison.OrdinalIgnoreCase))
                            addResult(objId);
                        break;
                    }
                    case StringCriterion.MatchType.Prefix:
                    {
                        var val = GetFldValue<char>(environment, transaction, objId, criterion.FieldId);

                        if (val.StartsWith(criterion.Value.AsSpan(), StringComparison.OrdinalIgnoreCase))
                            addResult(objId);
                        break;
                    }
                    case StringCriterion.MatchType.Postfix:
                    {
                        var val = GetFldValue<char>(environment, transaction, objId, criterion.FieldId);

                        if (val.EndsWith(criterion.Value.AsSpan(), StringComparison.OrdinalIgnoreCase))
                            addResult(objId);
                        break;
                    }
                    case StringCriterion.MatchType.Fuzzy:
                        //todo, implement ngram fuzzy search...
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                return true;
            });

            return;
        }


        using var cursor = transaction.CreateCursor(environment.StringSearchIndex);

        var strValue = Normalize(criterion.Value);

        if (criterion.Type == StringCriterion.MatchType.Exact)
        {
            var exactKey = ConstructStringIndexKey(IndexFlag.Normal, criterion.FieldId, strValue);
            Collect(exactKey, exact: true);
        }
        else if (criterion.Type == StringCriterion.MatchType.Prefix)
        {
            var prefixForwardKey = ConstructStringIndexKey(IndexFlag.Normal, criterion.FieldId, strValue);
            Collect(prefixForwardKey, exact: false);
        }
        else if (criterion.Type == StringCriterion.MatchType.Postfix)
        {
            var prefixBackwardKey = ConstructStringIndexKey(IndexFlag.Reverse, criterion.FieldId, strValue);
            Collect(prefixBackwardKey, exact: false);
        }
        else if (criterion.Type == StringCriterion.MatchType.Fuzzy && strValue.Length >= 3)
        {
            //this is very primitive fuzzy searching, we just count the number of ngram matches and if it is above a cutoff, we count it as a match.

            Dictionary<Guid, int> matchingObjs = [];

            for (int i = 0; i < strValue.Length - 2; i++)
            {
                var ngramKey = ConstructStringIndexKey(IndexFlag.NGram, criterion.FieldId, strValue.AsSpan().Slice(i, 3));

                if (cursor.SetKey(ngramKey).resultCode == MDBResultCode.Success)
                {
                    do
                    {
                        var (_, _, value) = cursor.GetCurrent();

                        var guid = MemoryMarshal.Read<Guid>(value.AsSpan());

                        if (matchingObjs.TryGetValue(guid, out var currentCount))
                        {
                            matchingObjs[guid] = currentCount + 1;
                        }
                        else
                        {
                            matchingObjs.Add(guid, 1);
                        }
                    } while (cursor.NextDuplicate().resultCode == MDBResultCode.Success);
                }
            }

            foreach (var (guid, ngramMatches) in matchingObjs)
            {
                var searchTermNgramCount = strValue.Length - 2;

                if ((float)ngramMatches / searchTermNgramCount >= criterion.FuzzyCutoff)
                {
                    addResult(guid);
                }
            }
        }
        else if (criterion.Type == StringCriterion.MatchType.Substring && strValue.Length >= 3)
        {
            //ngram search
            HashSet<Guid> lastRound = [];
            HashSet<Guid> thisRound = [];

            for (int i = 0; i < strValue.Length - 2; i++)
            {
                var ngramKey = ConstructStringIndexKey(IndexFlag.NGram, criterion.FieldId, strValue.AsSpan().Slice(i, 3));

                if (cursor.SetKey(ngramKey).resultCode == MDBResultCode.Success)
                {
                    do
                    {
                        var (_, _, value) = cursor.GetCurrent();

                        var guid = MemoryMarshal.Read<Guid>(value.AsSpan());

                        if (i == 0 || lastRound.Contains(guid))
                        {
                            thisRound.Add(guid);
                        }
                    } while (cursor.NextDuplicate().resultCode == MDBResultCode.Success);
                }

                if (thisRound.Count == 0)
                    break;

                (thisRound, lastRound) = (lastRound, thisRound);
                thisRound.Clear();
            }

            Span<byte> keyBuf = stackalloc byte[2 * 16];
            foreach (var guid in lastRound)
            {
                MemoryMarshal.Write(keyBuf.Slice(0 * 16, 16), guid);
                MemoryMarshal.Write(keyBuf.Slice(1 * 16, 16), criterion.FieldId);

                var (r, _, v) = transaction.Get(environment.ObjectDb, keyBuf);
                if (r == MDBResultCode.Success)
                {
                    if (MemoryMarshal.Cast<byte, char>(v.AsSpan().Slice(1)).Contains(strValue.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        addResult(guid);
                    }
                }
            }
        }

        void Collect(byte[] prefixKey, bool exact)
        {
            if (cursor.SetRange(prefixKey) == MDBResultCode.Success)
            {
                do
                {
                    var (_, key, value) = cursor.GetCurrent();

                    if (exact && !key.AsSpan().SequenceEqual(prefixKey))
                        break;

                    if (!key.AsSpan().StartsWith(prefixKey))
                        break;

                    addResult(MemoryMarshal.Read<Guid>(value.AsSpan()));
                } while (cursor.Next().resultCode == MDBResultCode.Success);
            }
        }
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

        MemoryMarshal.Write(destination.Slice(1 + 16), data);
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

    private static void InsertIndex(FieldDataType indexType, Guid objId, Guid fldId, ReadOnlySpan<byte> val, LightningTransaction txn, Environment environment)
    {
        switch (indexType)
        {
            case FieldDataType.String:
                InsertStringIndex(objId, fldId, MemoryMarshal.Cast<byte, char>(val), txn, environment.StringSearchIndex);
                break;
            case FieldDataType.DateTime:
                InsertNonStringIndex<DateTime>(CustomIndexComparer.Comparison.DateTime, fldId, objId, val, txn, environment.NonStringSearchIndex);
                break;
            case FieldDataType.Integer:
                InsertNonStringIndex<long>(CustomIndexComparer.Comparison.SignedLong, fldId, objId, val, txn, environment.NonStringSearchIndex);
                break;
            case FieldDataType.Decimal:
                InsertNonStringIndex<decimal>(CustomIndexComparer.Comparison.Decimal, fldId, objId, val, txn, environment.NonStringSearchIndex);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void InsertTypeIndex(Environment environment, Guid typId, LightningTransaction txn, Guid objId)
    {
        Span<byte> dest = stackalloc byte[1 + 16];
        dest[0] = (byte)CustomIndexComparer.Comparison.Type;
        typId.AsSpan().CopyTo(dest.Slice(1));

        txn.Put(environment.NonStringSearchIndex, dest, objId.AsSpan());
    }

    private static void InsertNonStringIndex<T>(CustomIndexComparer.Comparison comparison, Guid fldId, Guid objId, ReadOnlySpan<byte> val, LightningTransaction transaction, LightningDatabase indexDb) where T : unmanaged
    {
        Span<byte> dest = stackalloc byte[GetNonStringKeySize<T>()];
        ConstructNonStringIndexKey(comparison, fldId, MemoryMarshal.Read<T>(val), dest);
        transaction.Put(indexDb, dest, objId.AsSpan());
    }

    private static Guid RemoveNonStringIndexValue<T>(ReadOnlySpan<byte> oldValue, CustomIndexComparer.Comparison comparison, Guid fldId, Guid objId, LightningTransaction txn, LightningDatabase db) where T : unmanaged
    {
        Span<byte> dest = stackalloc byte[GetNonStringKeySize<T>()];
        ConstructNonStringIndexKey(comparison, fldId, MemoryMarshal.Read<T>(oldValue), dest);
        txn.Delete(db, dest, objId.AsSpan());

        return objId;
    }

    private static void InsertStringIndex(Guid objId, Guid fldId, ReadOnlySpan<char> str, LightningTransaction transaction, LightningDatabase indexDb)
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
        return input.ToString().ToLower(); //todo don't allocate two strings!!!
    }
}

public enum IndexFlag : byte
{
    Normal,
    Reverse,
    NGram
}