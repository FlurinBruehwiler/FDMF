using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using FDMF.Core;
using LightningDB;

namespace FDMF.Core.DatabaseLayer;

//key: [flag][fldid][value]:[obj]

//todo at some point we also want the functionality to search the non-commited data (these wouldn't need an index)

//todo the following features need to be implemented
// [x] implement search of non indexed values fields, ok the problem with this is, that with the current architecture,
//          objects are not grouped by type, we can still implement non-indexed search, but it is not very efficient.
//          We could try to either group by type, or even more extreme, group by field, which would lead to an SOA,
//          which would be very efficient when searching. The problem its, that this would make other things more complex,
//          such as deleting objs, so for now we could just stick with the current layout. Another possibility,
//          is to make the type part of the objId, so that start of any ObjId, is actually its type.
// [ ] implement ranking (only relevant for fuzzy search I think)
// [ ] implement partial results
// [ ] implement result info (for example in a substring search, we want to see the part of the substring that matched
// [ ] implement profiling (we want to see what searches are slow, so the user can add indexes to these fields
// [x] implement more complex operators (not only AND, but also OR)
// [x] implement sub queries
// [x] implement default value search (assoc null/not null, 0/not 0, empty datetime/nonempty datetime)
// [ ] implement better substring/fuzzy search
// [ ] improve the performance (reducing allocations)
// [x] implement search by type (indexed)

/// <summary>
/// Method for Searching the Database and maintaining indexes.
/// </summary>
public static class Searcher
{
    public static List<T> Search<T>(DbSession dbSession, ISearchCriterion? searchQuery = null, int maxResults = int.MaxValue) where T : ITransactionObject, new()
    {
        var result = new List<T>();

        int count = 0;
        SearchInternal(dbSession, new SearchQuery
        {
            TypId = T.TypId,
            SearchCriterion = searchQuery
        }, id =>
        {
            result.Add(new T
            {
                DbSession = dbSession,
                ObjId = id
            });

            count++;

            return count < maxResults;
        });

        return result;
    }

    public static void BuildIndex(DbSession session)
    {
        var environment = session.DbEnvironment;

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

                    // Field is present (has a VAL entry).
                    txn.Put(environment.FieldPresenceIndex, fldId.AsSpan(), objId.AsSpan());

                    var fld = session.GetObjFromGuid<FieldDefinition>(fldId)!.Value;
                    if (fld.IsIndexed)
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
    public static void UpdateSearchIndex(DbSession session, LightningTransaction txn, BPlusTree changeSet)
    {
        var environment = session.DbEnvironment;
        var model = session.GetObjFromGuid<Model>(environment.ModelGuid)!.Value;
        var fieldsById = model.GetAllFieldDefinitions().ToDictionary(x => x.Id, x => x);

        var changeCursor = changeSet.CreateCursor();
        using var baseCursor = txn.CreateCursor(environment.ObjectDb);

        Span<byte> startKey = stackalloc byte[2];
        startKey[0] = 0;
        startKey[1] = 0;

        if (changeCursor.SetRange(startKey) == ResultCode.Success)
        {
            do
            {
                var (_, keyWithFlag, value) = changeCursor.GetCurrent();

                if (keyWithFlag.Length == 0)
                    continue;

                var flag = (ValueFlag)keyWithFlag[^1];
                var key = keyWithFlag.Slice(0, keyWithFlag.Length - 1);

                if (key.Length < 16)
                    continue;

                var objId = MemoryMarshal.Read<Guid>(key);

                if (key.Length == 16) //obj
                {
                    txn.Delete(environment.ObjectDb, objId.AsSpan());

                    if (flag == ValueFlag.AddModify)
                    {
                        var typId = MemoryMarshal.Read<Guid>(value.Slice(1));
                        InsertTypeIndex(environment, typId, txn, objId);
                    }
                }
                else if (key.Length == 32) //val
                {
                    var fldId = MemoryMarshal.Read<Guid>(key.Slice(16));

                    var (r, _, v) = txn.Get(environment.ObjectDb, key);
                    bool oldExists = r == MDBResultCode.Success;

                    if (oldExists && v.AsSpan().Length > 0 && v.AsSpan()[0] == (byte)ValueTyp.Val)
                    {
                        // Transition: present -> missing
                        if (flag == ValueFlag.Delete)
                        {
                            txn.Delete(environment.FieldPresenceIndex, fldId.AsSpan(), objId.AsSpan());
                        }

                        // If the field is indexed, remove the old value from its search index.
                        if (fieldsById.TryGetValue(fldId, out var fieldDefinition) && fieldDefinition.IsIndexed)
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
                                    RemoveNonStringIndexValue<DateTime>(oldValue, CustomIndexComparer.Comparison.DateTime, fldId, objId, txn, environment.NonStringSearchIndex);
                                    break;
                                case FieldDataType.Integer:
                                    RemoveNonStringIndexValue<long>(oldValue, CustomIndexComparer.Comparison.SignedLong, fldId, objId, txn, environment.NonStringSearchIndex);
                                    break;
                                case FieldDataType.Decimal:
                                    RemoveNonStringIndexValue<decimal>(oldValue, CustomIndexComparer.Comparison.Decimal, fldId, objId, txn, environment.NonStringSearchIndex);
                                    break;
                                case FieldDataType.Boolean:
                                    RemoveNonStringIndexValue<bool>(oldValue, CustomIndexComparer.Comparison.Boolean, fldId, objId, txn, environment.NonStringSearchIndex);
                                    break;
                                case FieldDataType.Guid:
                                    throw new NotImplementedException();
                                case FieldDataType.Enum:
                                    throw new NotImplementedException();
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }
                    }

                    // Transition: missing -> present
                    if (!oldExists && flag == ValueFlag.AddModify)
                    {
                        txn.Put(environment.FieldPresenceIndex, fldId.AsSpan(), objId.AsSpan());
                    }

                    // If the field is indexed, insert the new value.
                    if (flag == ValueFlag.AddModify && fieldsById.TryGetValue(fldId, out var newFieldDefinition) && newFieldDefinition.IsIndexed)
                    {
                        var val = value.Slice(1);
                        InsertIndex(newFieldDefinition.DataType, objId, fldId, val, txn, environment);
                    }
                }
            } while (changeCursor.Next().ResultCode == ResultCode.Success);
        }
    }

    private static bool MatchCriterion(DbSession dbSession, ISearchCriterion criterion, Guid obj)
    {
        switch (criterion)
        {
            case AssocCriterion assocCriterion:
                switch (assocCriterion.Type)
                {
                    case AssocCriterion.AssocCriterionType.Subquery:
                        if (assocCriterion.SearchCriterion != null)
                        {
                            return MatchCriterion(dbSession, assocCriterion.SearchCriterion, obj);
                        }

                        Logging.Log(LogFlags.Error, "Subquery needs a searchCriterion");
                        break;
                    case AssocCriterion.AssocCriterionType.Null:
                    {
                        using var cursor = dbSession.Store.ReadTransaction.CreateCursor(dbSession.DbEnvironment.ObjectDb);

                        Span<byte> prefix = stackalloc byte[2 * 16];
                        MemoryMarshal.Write(prefix, obj);
                        MemoryMarshal.Write(prefix.Slice(16), assocCriterion.FieldId);

                        if (cursor.SetRange(prefix) == MDBResultCode.Success)
                        {
                            var (_, key, _) = cursor.GetCurrent();
                            var keySpan = key.AsSpan();
                            if (keySpan.Length == 4 * 16 && prefix.SequenceEqual(keySpan.Slice(0, 2 * 16)))
                                return false;
                        }

                        return true;
                    }
                    case AssocCriterion.AssocCriterionType.NotNull:
                    {
                        using var cursor = dbSession.Store.ReadTransaction.CreateCursor(dbSession.DbEnvironment.ObjectDb);

                        Span<byte> prefix = stackalloc byte[2 * 16];
                        MemoryMarshal.Write(prefix, obj);
                        MemoryMarshal.Write(prefix.Slice(16), assocCriterion.FieldId);

                        if (cursor.SetRange(prefix) == MDBResultCode.Success)
                        {
                            var (_, key, _) = cursor.GetCurrent();
                            var keySpan = key.AsSpan();
                            if (keySpan.Length == 4 * 16 && prefix.SequenceEqual(keySpan.Slice(0, 2 * 16)))
                                return true;
                        }

                        return false;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                return false;
            case DateTimeCriterion dateTimeCriterion:
                return MatchNonStringSearch(dbSession.DbEnvironment, dbSession.Store.ReadTransaction, obj, dateTimeCriterion.FieldId, dateTimeCriterion.From, dateTimeCriterion.To);
            case DecimalCriterion decimalCriterion:
                return MatchNonStringSearch(dbSession.DbEnvironment, dbSession.Store.ReadTransaction, obj, decimalCriterion.FieldId, decimalCriterion.From, decimalCriterion.To);
            case BooleanCriterion booleanCriterion:
                return MatchBooleanCriterion(dbSession.DbEnvironment, dbSession.Store.ReadTransaction, obj, booleanCriterion);
            case IdCriterion idCriterion:
                return obj == idCriterion.Guid;
            case LongCriterion longCriterion:
                return MatchNonStringSearch(dbSession.DbEnvironment, dbSession.Store.ReadTransaction, obj, longCriterion.FieldId, longCriterion.From, longCriterion.To);
            case MultiCriterion { Type: MultiCriterion.MultiType.OR } multiCriterion:
                foreach (var crit in multiCriterion.Criterions)
                {
                    if (MatchCriterion(dbSession, crit, obj))
                        return true;
                }

                return false;
            case MultiCriterion { Type: MultiCriterion.MultiType.XOR } multiCriterion:
                int matches = 0;
                foreach (var crit in multiCriterion.Criterions)
                {
                    if (MatchCriterion(dbSession, crit, obj))
                        matches++;
                }

                return matches == 1;
            case MultiCriterion { Type: MultiCriterion.MultiType.AND } multiCriterion:
                foreach (var crit in multiCriterion.Criterions)
                {
                    if (!MatchCriterion(dbSession, crit, obj))
                        return false;
                }

                return true;
            case SearchQuery searchQuery:
                if (searchQuery.SearchCriterion == null)
                    return GeneratedCodeHelper.IsAssignableFrom(dbSession, searchQuery.TypId, dbSession.GetTypId(obj));

                if (!GeneratedCodeHelper.IsAssignableFrom(dbSession, searchQuery.TypId, dbSession.GetTypId(obj)))
                    return false;
                return MatchCriterion(dbSession, searchQuery.SearchCriterion, obj);
            case StringCriterion stringCriterion:
                return MatchStringCriterion(dbSession.DbEnvironment, dbSession.Store.ReadTransaction, obj, stringCriterion);
            default:
                throw new ArgumentOutOfRangeException(nameof(criterion));
        }
    }

    [SuppressMessage("ReSharper", "AccessToModifiedClosure")]
    private static bool SearchInternal(DbSession dbSession, ISearchCriterion criterion, Func<Guid, bool> addResult)
    {
        switch (criterion)
        {
            case IdCriterion idCriterion:
                if (dbSession.GetTypId(idCriterion.Guid) != Guid.Empty)
                {
                    return addResult(idCriterion.Guid);
                }

                return true;
            case SearchQuery searchQuery:
                if (searchQuery.SearchCriterion == null)
                {
                    return ExecuteTypeSearchAssignable(dbSession, dbSession.Store.ReadTransaction, searchQuery.TypId, addResult);
                }

                // Use the inner criterion for candidate generation (indexes), but still enforce the type constraint.
                return SearchInternal(dbSession, searchQuery.SearchCriterion, guid =>
                {
                    if (GeneratedCodeHelper.IsAssignableFrom(dbSession, searchQuery.TypId, dbSession.GetTypId(guid)))
                        return addResult(guid);

                    return true;
                });

            case MultiCriterion { Type: MultiCriterion.MultiType.OR } multiCriterion:

                HashSet<Guid> seen = [];

                foreach (var crit in multiCriterion.Criterions)
                {
                    var shouldContinue = SearchInternal(dbSession, crit, guid =>
                    {
                        if (seen.Add(guid))
                        {
                            if (!addResult(guid))
                            {
                                return false;
                            }
                        }

                        return true;
                    });

                    if (!shouldContinue)
                        return false;
                }

                return true;
            case MultiCriterion { Type: MultiCriterion.MultiType.AND } multiCriterion:

                HashSet<Guid>? workingSet = null;

                foreach (var crit in multiCriterion.Criterions.OrderBy(x => EstimateCriterionCost(dbSession, x)))
                {
                    if (workingSet == null)
                    {
                        workingSet = [];
                        SearchInternal(dbSession, crit, g =>
                        {
                            workingSet.Add(g);
                            return true;
                        });
                    }
                    else
                    {
                        workingSet.RemoveWhere(g => !MatchCriterion(dbSession, crit, g));
                    }
                }

                if (workingSet != null)
                {
                    foreach (var guid in workingSet)
                    {
                        if (!addResult(guid))
                        {
                            return false;
                        }
                    }
                }

                return true;
            case MultiCriterion { Type: MultiCriterion.MultiType.XOR } multiCriterion:
                HashSet<Guid> previous = [];
                HashSet<Guid> current = [];

                int i = 0;

                foreach (var crit in multiCriterion.Criterions)
                {
                    SearchInternal(dbSession, crit, (guid) =>
                    {
                        if (i == 0 || !previous.Contains(guid))
                        {
                            current.Add(guid);
                        }

                        return true;
                    });


                    i++;
                    (previous, current) = (current, previous);

                    current.Clear();
                }

                foreach (var guid in previous)
                {
                    if (!addResult(guid))
                    {
                        return false;
                    }
                }

                return true;
            case AssocCriterion assocCriterion:
                return ExecuteAssocSearch(dbSession, assocCriterion, addResult);
            case DateTimeCriterion dateTimeCriterion:
                return ExecuteNonStringSearch(dbSession, dbSession.Store.ReadTransaction, addResult, CustomIndexComparer.Comparison.DateTime, dateTimeCriterion.FieldId, dateTimeCriterion.From, dateTimeCriterion.To);
            case DecimalCriterion decimalCriterion:
                return ExecuteNonStringSearch(dbSession, dbSession.Store.ReadTransaction, addResult, CustomIndexComparer.Comparison.Decimal, decimalCriterion.FieldId, decimalCriterion.From, decimalCriterion.To);
            case LongCriterion longCriterion:
                return ExecuteNonStringSearch(dbSession, dbSession.Store.ReadTransaction, addResult, CustomIndexComparer.Comparison.SignedLong, longCriterion.FieldId, longCriterion.From, longCriterion.To);
            case BooleanCriterion booleanCriterion:
                return ExecuteBooleanSearch(dbSession, dbSession.Store.ReadTransaction, booleanCriterion, addResult);
            case StringCriterion stringCriterion:
                return ExecuteStringSearch(dbSession, dbSession.Store.ReadTransaction, stringCriterion, addResult);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static int EstimateCriterionCost(DbSession dbSession, ISearchCriterion searchCriterion)
    {
        switch (searchCriterion)
        {
            case StringCriterion stringCriterion:
                if (dbSession.GetObjFromGuid<FieldDefinition>(stringCriterion.FieldId)!.Value.IsIndexed)
                    return 0;
                return 1000;
            case AssocCriterion assocCriterion:
                if (assocCriterion.SearchCriterion != null)
                    return 50 + EstimateCriterionCost(dbSession, assocCriterion.SearchCriterion);

                return 50;
            case DateTimeCriterion dateTimeCriterion:
                if (dbSession.GetObjFromGuid<FieldDefinition>(dateTimeCriterion.FieldId)!.Value.IsIndexed)
                    return 0;
                return 1000;
            case DecimalCriterion decimalCriterion:
                if (dbSession.GetObjFromGuid<FieldDefinition>(decimalCriterion.FieldId)!.Value.IsIndexed)
                    return 0;
                return 1000;
            case IdCriterion:
                return 0;
            case LongCriterion longCriterion:
                if (dbSession.GetObjFromGuid<FieldDefinition>(longCriterion.FieldId)!.Value.IsIndexed)
                    return 0;
                return 1000;
            case BooleanCriterion booleanCriterion:
                if (dbSession.GetObjFromGuid<FieldDefinition>(booleanCriterion.FieldId)!.Value.IsIndexed)
                    return 0;
                return 1000;
            default:
                throw new ArgumentOutOfRangeException(searchCriterion + nameof(searchCriterion));
        }
    }

    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    private static bool ExecuteAssocSearch(DbSession dbSession, AssocCriterion criterion, Func<Guid, bool> addResult)
    {
        DbEnvironment env = dbSession.DbEnvironment;
        LightningTransaction transaction = dbSession.Store.ReadTransaction;


        var fld = dbSession.GetObjFromGuid<ReferenceFieldDefinition>(criterion.FieldId);

        using var cursor = transaction.CreateCursor(env.ObjectDb);

        switch (criterion.Type)
        {
            case AssocCriterion.AssocCriterionType.Subquery:
                if (criterion.SearchCriterion != null)
                {
                    //todo do we want to validate if this criterion is even valid, i.e. the object actually has the fields
                    return SearchInternal(dbSession, criterion.SearchCriterion, objId =>
                    {
                        Span<byte> key = stackalloc byte[2 * 16];
                        MemoryMarshal.Write(key, objId);
                        MemoryMarshal.Write(key.Slice(16), fld!.Value.OtherReferenceFields.ObjId);

                        if (cursor.SetRange(key) == MDBResultCode.Success)
                        {
                            do
                            {
                                var (_, k, _) = cursor.GetCurrent();

                                if (k.AsSpan().Length != 64 || !key.SequenceEqual(k.AsSpan().Slice(0, 2 * 16)))
                                    break;

                                if (!addResult(MemoryMarshal.Read<Guid>(k.AsSpan().Slice(2 * 16, 16))))
                                {
                                    return false;
                                }
                            } while (cursor.Next().resultCode == MDBResultCode.Success);
                        }

                        return true;
                    });
                }
                else
                {
                    Logging.Log(LogFlags.Error, "A SearchCriterion needs to be specified if the search type is Subquery");
                    return true;
                }

                break;
            case AssocCriterion.AssocCriterionType.Null:
            {
                // Not indexed for now.
                // Returns all objects of the owning entity where the association has no entries.
                return ExecuteTypeSearchAssignable(dbSession, transaction, fld!.Value.OwningEntity.Id, objId =>
                {
                    Span<byte> key = stackalloc byte[2 * 16];
                    MemoryMarshal.Write(key, objId);
                    MemoryMarshal.Write(key.Slice(16), criterion.FieldId);

                    bool hasAssoc = false;
                    if (cursor.SetRange(key) == MDBResultCode.Success)
                    {
                        var (_, k, _) = cursor.GetCurrent();
                        if (k.AsSpan().Length == 4 * 16 && key.SequenceEqual(k.AsSpan().Slice(0, 2 * 16)))
                            hasAssoc = true;
                    }

                    if (!hasAssoc)
                        return addResult(objId);

                    return true;
                });
            }
            case AssocCriterion.AssocCriterionType.NotNull:
            {
                // Not indexed for now.
                // Returns all objects of the owning entity where the association has at least one entry.
                return ExecuteTypeSearchAssignable(dbSession, transaction, fld!.Value.OwningEntity.Id, objId =>
                {
                    Span<byte> key = stackalloc byte[2 * 16];
                    MemoryMarshal.Write(key, objId);
                    MemoryMarshal.Write(key.Slice(16), criterion.FieldId);

                    bool hasAssoc = false;
                    if (cursor.SetRange(key) == MDBResultCode.Success)
                    {
                        var (_, k, _) = cursor.GetCurrent();
                        if (k.AsSpan().Length == 4 * 16 && key.SequenceEqual(k.AsSpan().Slice(0, 2 * 16)))
                            hasAssoc = true;
                    }

                    if (hasAssoc)
                        return addResult(objId);

                    return true;
                });
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static bool MatchNonStringSearch<T>(DbEnvironment dbEnvironment, LightningTransaction transaction, Guid objId, Guid fieldId, T from, T to) where T : unmanaged, IComparable<T>
    {
        var val = GetFldValue<byte>(dbEnvironment, transaction, objId, fieldId);

        if (val.Length == 0)
        {
            // Missing value behaves like default(T).
            var def = default(T);
            return def.CompareTo(from) >= 0 && def.CompareTo(to) <= 0;
        }

        if (CustomIndexComparer.CompareGeneric<T>(val, from.AsSpan()) >= 0)
        {
            if (CustomIndexComparer.CompareGeneric<T>(val, to.AsSpan()) <= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExecuteNonStringSearch<T>(DbSession dbSession, LightningTransaction transaction, Func<Guid, bool> addResult, CustomIndexComparer.Comparison comparison, Guid fieldId, T from, T to) where T : unmanaged, IComparable<T>
    {
        var fld = dbSession.GetObjFromGuid<FieldDefinition>(fieldId);

        if (fld == default)
        {
            Logging.Log(LogFlags.Error, $"There isn't a Field with the ID {fieldId}");
            return true;
        }

        if (!fld!.Value.IsIndexed)
        {
            return ExecuteTypeSearchAssignable(dbSession, transaction, fld!.Value.OwningEntity.Id, (objId) =>
            {
                if (MatchNonStringSearch(dbSession.DbEnvironment, transaction, objId, fieldId, from, to))
                {
                    if (!addResult(objId))
                    {
                        return false;
                    }
                }

                return true;
            });
        }

        HashSet<Guid>? seen = null;

        using var cursor = transaction.CreateCursor(dbSession.DbEnvironment.NonStringSearchIndex);

        Span<byte> minKey = stackalloc byte[GetNonStringKeySize<T>()];
        ConstructNonStringIndexKey(comparison, fieldId, from, minKey);

        Span<byte> maxKey = stackalloc byte[GetNonStringKeySize<T>()];
        ConstructNonStringIndexKey(comparison, fieldId, to, maxKey);

        bool includesDefault = default(T).CompareTo(from) >= 0 && default(T).CompareTo(to) <= 0;
        if (includesDefault)
            seen = [];

        if (cursor.SetRange(minKey) == MDBResultCode.Success)
        {
            do
            {
                var (_, key, value) = cursor.GetCurrent();

                if (CustomIndexComparer.CompareStatic(maxKey, key.AsSpan()) < 0)
                    break;

                var id = MemoryMarshal.Read<Guid>(value.AsSpan());
                if (seen == null || seen.Add(id))
                {
                    if (!addResult(id))
                    {
                        return false;
                    }
                }
            } while (cursor.Next().resultCode == MDBResultCode.Success);
        }

        if (!includesDefault)
            return true;

        return AddMissingFieldValuesAssignable(dbSession, transaction, fld!.Value.OwningEntity.Id, fieldId, seen!, addResult);
    }

    private static bool ExecuteTypeSearchExact(DbEnvironment dbEnvironment, LightningTransaction transaction, Guid typId, Func<Guid, bool> addResult)
    {
        using var cursor = transaction.CreateCursor(dbEnvironment.NonStringSearchIndex);

        Span<byte> dest = stackalloc byte[1 + 16];
        dest[0] = (byte)CustomIndexComparer.Comparison.Type;
        typId.AsSpan().CopyTo(dest.Slice(1));


        if (cursor.SetKey(dest).resultCode == MDBResultCode.Success)
        {
            do
            {
                var (_, _, value) = cursor.GetCurrent();

                var guid = MemoryMarshal.Read<Guid>(value.AsSpan());

                if (!addResult(guid))
                {
                    return false;
                }
            } while (cursor.NextDuplicate().resultCode == MDBResultCode.Success);
        }

        return true;
    }

    private static bool ExecuteTypeSearchAssignable(DbSession dbSession, LightningTransaction transaction, Guid typId, Func<Guid, bool> addResult)
    {
        HashSet<Guid> seenTypes = [];
        Queue<Guid> queue = new();

        queue.Enqueue(typId);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            if (!seenTypes.Add(t))
                continue;

            var ed = dbSession.GetObjFromGuid<EntityDefinition>(t);
            if (ed.HasValue)
            {
                foreach (var child in ed.Value.Children)
                    queue.Enqueue(child.ObjId);
            }
        }

        HashSet<Guid> seenObjs = [];
        foreach (var t in seenTypes)
        {
            if (!ExecuteTypeSearchExact(dbSession.DbEnvironment, transaction, t, objId =>
                {
                    if (seenObjs.Add(objId))
                        return addResult(objId);
                    return true;
                }))
            {
                return false;
            }
        }

        return true;
    }

    private static ReadOnlySpan<T> GetFldValue<T>(DbEnvironment dbEnvironment, LightningTransaction transaction, Guid objId, Guid fldId) where T : unmanaged
    {
        Span<byte> keyBuf = stackalloc byte[2 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0 * 16, 16), objId);
        MemoryMarshal.Write(keyBuf.Slice(1 * 16, 16), fldId);

        var (r, _, v) = transaction.Get(dbEnvironment.ObjectDb, keyBuf);
        if (r == MDBResultCode.Success)
        {
            return MemoryMarshal.Cast<byte, T>(v.AsSpan().Slice(1));
        }

        return ReadOnlySpan<T>.Empty;
    }

    private static bool AddMissingFieldValuesAssignable(DbSession dbSession, LightningTransaction transaction, Guid typId, Guid fieldId, HashSet<Guid> seen, Func<Guid, bool> addResult)
    {
        var typesSeen = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(typId);

        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            if (!typesSeen.Add(t))
                continue;

            var ed = dbSession.GetObjFromGuid<EntityDefinition>(t);
            if (ed.HasValue)
            {
                foreach (var child in ed.Value.Children)
                    queue.Enqueue(child.ObjId);
            }
        }

        foreach (var t in typesSeen)
        {
            if (!AddMissingFieldValuesExact(dbSession.DbEnvironment, transaction, t, fieldId, seen, addResult))
                return false;
        }

        return true;
    }

    private static bool AddMissingFieldValuesExact(DbEnvironment dbEnvironment, LightningTransaction transaction, Guid typId, Guid fieldId, HashSet<Guid> seen, Func<Guid, bool> addResult)
    {
        using var typeCursor = transaction.CreateCursor(dbEnvironment.NonStringSearchIndex);

        Span<byte> typeKey = stackalloc byte[1 + 16];
        typeKey[0] = (byte)CustomIndexComparer.Comparison.Type;
        typId.AsSpan().CopyTo(typeKey.Slice(1));

        if (typeCursor.SetKey(typeKey).resultCode != MDBResultCode.Success)
            return true;

        using var presenceCursor = transaction.CreateCursor(dbEnvironment.FieldPresenceIndex);

        bool hasPresence = presenceCursor.SetKey(fieldId.AsSpan()).resultCode == MDBResultCode.Success;
        Guid currentPresence = default;
        if (hasPresence)
        {
            var (_, _, value) = presenceCursor.GetCurrent();
            currentPresence = MemoryMarshal.Read<Guid>(value.AsSpan());
        }

        do
        {
            var (_, _, typeValue) = typeCursor.GetCurrent();
            var objId = MemoryMarshal.Read<Guid>(typeValue.AsSpan());

            while (hasPresence && BPlusTree.CompareLexicographic(currentPresence.AsSpan(), objId.AsSpan()) < 0)
            {
                hasPresence = presenceCursor.NextDuplicate().resultCode == MDBResultCode.Success;
                if (hasPresence)
                {
                    var (_, _, v) = presenceCursor.GetCurrent();
                    currentPresence = MemoryMarshal.Read<Guid>(v.AsSpan());
                }
            }

            bool isPresent = hasPresence && currentPresence == objId;
            if (!isPresent && seen.Add(objId))
            {
                if (!addResult(objId))
                {
                    return false;
                }
            }
        } while (typeCursor.NextDuplicate().resultCode == MDBResultCode.Success);

        return true;
    }

    private static bool MatchBooleanCriterion(DbEnvironment dbEnvironment, LightningTransaction transaction, Guid objId, BooleanCriterion criterion)
    {
        var val = GetFldValue<byte>(dbEnvironment, transaction, objId, criterion.FieldId);

        if (val.Length == 0)
        {
            // Missing value behaves like default(bool) == false.
            return criterion.Value == false;
        }

        return MemoryMarshal.Read<bool>(val) == criterion.Value;
    }

    private static bool MatchStringCriterion(DbEnvironment dbEnvironment, LightningTransaction transaction, Guid objId, StringCriterion criterion)
    {
        switch (criterion.Type)
        {
            case StringCriterion.MatchType.Substring:
            {
                var val = GetFldValue<char>(dbEnvironment, transaction, objId, criterion.FieldId);

                return val.Contains(criterion.Value.AsSpan(), StringComparison.OrdinalIgnoreCase);
            }
            case StringCriterion.MatchType.Exact:
            {
                var val = GetFldValue<char>(dbEnvironment, transaction, objId, criterion.FieldId);

                return val.Equals(criterion.Value.AsSpan(), StringComparison.OrdinalIgnoreCase);
            }
            case StringCriterion.MatchType.Prefix:
            {
                var val = GetFldValue<char>(dbEnvironment, transaction, objId, criterion.FieldId);

                return val.StartsWith(criterion.Value.AsSpan(), StringComparison.OrdinalIgnoreCase);
            }
            case StringCriterion.MatchType.Postfix:
            {
                var val = GetFldValue<char>(dbEnvironment, transaction, objId, criterion.FieldId);

                return val.EndsWith(criterion.Value.AsSpan(), StringComparison.OrdinalIgnoreCase);
            }
            case StringCriterion.MatchType.Fuzzy:
                //todo, implement ngram fuzzy search...
                return false;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static bool ExecuteBooleanSearch(DbSession dbSession, LightningTransaction transaction, BooleanCriterion criterion, Func<Guid, bool> addResult)
    {
        var fld = dbSession.GetObjFromGuid<FieldDefinition>(criterion.FieldId);
        if (fld == default)
        {
            Logging.Log(LogFlags.Error, $"There isn't a Field with the ID {criterion.FieldId}");
            return true;
        }

        if (!fld!.Value.IsIndexed)
        {
            return ExecuteTypeSearchAssignable(dbSession, transaction, fld!.Value.OwningEntity.Id, objId =>
            {
                if (MatchBooleanCriterion(dbSession.DbEnvironment, transaction, objId, criterion))
                {
                    if (!addResult(objId))
                        return false;
                }

                return true;
            });
        }

        HashSet<Guid>? seen = null;
        if (criterion.Value == false)
            seen = [];

        using var cursor = transaction.CreateCursor(dbSession.DbEnvironment.NonStringSearchIndex);

        Span<byte> key = stackalloc byte[GetNonStringKeySize<bool>()];
        ConstructNonStringIndexKey(CustomIndexComparer.Comparison.Boolean, criterion.FieldId, criterion.Value, key);

        if (cursor.SetKey(key).resultCode == MDBResultCode.Success)
        {
            do
            {
                var (_, _, value) = cursor.GetCurrent();
                var id = MemoryMarshal.Read<Guid>(value.AsSpan());

                if (seen == null || seen.Add(id))
                {
                    if (!addResult(id))
                        return false;
                }
            } while (cursor.NextDuplicate().resultCode == MDBResultCode.Success);
        }

        if (criterion.Value == false)
        {
            return AddMissingFieldValuesAssignable(dbSession, transaction, fld!.Value.OwningEntity.Id, criterion.FieldId, seen!, addResult);
        }

        return true;
    }

    private static bool ExecuteStringSearch(DbSession dbSession, LightningTransaction transaction, StringCriterion criterion, Func<Guid, bool> addResult)
    {
        var fld = dbSession.GetObjFromGuid<FieldDefinition>(criterion.FieldId);

        if (!fld!.Value.IsIndexed)
        {
            return ExecuteTypeSearchAssignable(dbSession, transaction, fld!.Value.OwningEntity.Id, (objId) =>
            {
                if (MatchStringCriterion(dbSession.DbEnvironment, transaction, objId, criterion))
                {
                    if (!addResult(objId))
                    {
                        return false;
                    }
                }

                return true;
            });
        }


        using var cursor = transaction.CreateCursor(dbSession.DbEnvironment.StringSearchIndex);

        var strValue = Normalize(criterion.Value);

        if (criterion.Type == StringCriterion.MatchType.Exact)
        {
            var exactKey = ConstructStringIndexKey(IndexFlag.Normal, criterion.FieldId, strValue);

            HashSet<Guid>? seen = null;
            if (strValue.Length == 0)
                seen = [];

            if (!Collect(exactKey, exact: true, seen))
                return false;

            if (strValue.Length == 0)
            {
                return AddMissingFieldValuesAssignable(dbSession, transaction, fld!.Value.OwningEntity.Id, criterion.FieldId, seen!, addResult);
            }

            return true;
        }

        if (criterion.Type == StringCriterion.MatchType.Prefix)
        {
            var prefixForwardKey = ConstructStringIndexKey(IndexFlag.Normal, criterion.FieldId, strValue);
            return Collect(prefixForwardKey, exact: false, seen: null);
        }

        if (criterion.Type == StringCriterion.MatchType.Postfix)
        {
            var prefixBackwardKey = ConstructStringIndexKey(IndexFlag.Reverse, criterion.FieldId, strValue);
            return Collect(prefixBackwardKey, exact: false, seen: null);
        }

        if (criterion.Type == StringCriterion.MatchType.Fuzzy && strValue.Length >= 3)
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
                    if (!addResult(guid))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        if (criterion.Type == StringCriterion.MatchType.Substring && strValue.Length >= 3)
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

                var (r, _, v) = transaction.Get(dbSession.DbEnvironment.ObjectDb, keyBuf);
                if (r == MDBResultCode.Success)
                {
                    if (MemoryMarshal.Cast<byte, char>(v.AsSpan().Slice(1)).Contains(strValue.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        if (!addResult(guid))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        return true;

        bool Collect(byte[] prefixKey, bool exact, HashSet<Guid>? seen = null)
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

                    var guid = MemoryMarshal.Read<Guid>(value.AsSpan());
                    if (seen == null || seen.Add(guid))
                    {
                        if (!addResult(guid))
                        {
                            return false;
                        }
                    }
                } while (cursor.Next().resultCode == MDBResultCode.Success);
            }

            return true;
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

    private static void InsertIndex(FieldDataType indexType, Guid objId, Guid fldId, ReadOnlySpan<byte> val, LightningTransaction txn, DbEnvironment dbEnvironment)
    {
        switch (indexType)
        {
            case FieldDataType.String:
                InsertStringIndex(objId, fldId, MemoryMarshal.Cast<byte, char>(val), txn, dbEnvironment.StringSearchIndex);
                break;
            case FieldDataType.DateTime:
                InsertNonStringIndex<DateTime>(CustomIndexComparer.Comparison.DateTime, fldId, objId, val, txn, dbEnvironment.NonStringSearchIndex);
                break;
            case FieldDataType.Integer:
                InsertNonStringIndex<long>(CustomIndexComparer.Comparison.SignedLong, fldId, objId, val, txn, dbEnvironment.NonStringSearchIndex);
                break;
            case FieldDataType.Decimal:
                InsertNonStringIndex<decimal>(CustomIndexComparer.Comparison.Decimal, fldId, objId, val, txn, dbEnvironment.NonStringSearchIndex);
                break;
            case FieldDataType.Boolean:
                InsertNonStringIndex<bool>(CustomIndexComparer.Comparison.Boolean, fldId, objId, val, txn, dbEnvironment.NonStringSearchIndex);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void InsertTypeIndex(DbEnvironment dbEnvironment, Guid typId, LightningTransaction txn, Guid objId)
    {
        Span<byte> dest = stackalloc byte[1 + 16];
        dest[0] = (byte)CustomIndexComparer.Comparison.Type;
        typId.AsSpan().CopyTo(dest.Slice(1));

        txn.Put(dbEnvironment.NonStringSearchIndex, dest, objId.AsSpan());
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
