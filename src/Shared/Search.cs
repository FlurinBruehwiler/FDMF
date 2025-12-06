using System.Diagnostics;
using System.Runtime.InteropServices;
using Shared.Database;

namespace Shared;

public struct FieldCriterion
{
    public Guid FieldId;
    public string Value;
}

public static class Searcher
{
    public static IEnumerable<T> Search<T>(Transaction transaction, params FieldCriterion[] criteria) where T : ITransactionObject, new()
    {
        foreach (var (obj, typId) in transaction.EnumerateObjs())
        {
            if(T.TypId != typId)
                continue;

            bool matches = true;
            foreach (var queryCriterion in criteria)
            {
                var result = transaction.GetFldValue(obj, queryCriterion.FieldId);
                if (!result.AsSpan().SequenceEqual(MemoryMarshal.AsBytes(queryCriterion.Value.AsSpan())))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                yield return new T
                {
                    _objId = obj,
                    _transaction = transaction
                };
        }
    }
}