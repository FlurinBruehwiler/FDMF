using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FDMF.Core.Utils;
using LightningDB;

namespace FDMF.Core.DatabaseLayer;

//todo the following things need to be implemented
// [ ] implement updated/created at/by
// [ ] implement history
// [ ] think about extensibility / plugins, do we need some kind of scoping
// [ ] do we want some kind of validation of the db, that checks if all constraints are met
// [ ] implement inheritance / unions
// [x] implement readonly transactions
// [x] improve kvstore to work with less allocations

//todo do we want/need locks in here to ensure that there aren't multiple threads at the same time interacting with the transaction

/// <summary>
/// This represents a "long lived" transaction, there can multiple of these write transactions at the same time.
/// Specifically, it reads from the LMDB DB with a changeset layered ontop.
/// Once Commit is called, a LMDB write transaction is created (behind a lock) and the changeset is written to LMDB.
/// This replicates the LMDB API, we could put it behind an interface so that the three KV stores (LMDB, ChangeSet, LMDB + Changeset) have the same api.
/// </summary>
public sealed class DbSession : IDisposable
{
    /*
     * The objs are stored as follows:
     *
     * Keys:                                   Values: TODO, document how the values are stored
     * OBJ: ObjId:                             [F]TypId
     * ASO: ObjIdA, FldIdA, ObjIdB, FldIdB
     * ASO: ObjIdB, FldIdB, ObjIdA, FldIdA
     * VAL: ObjId, FldId
     *
     */

    public TransactionalKvStore.Cursor Cursor;
    public TransactionalKvStore Store;
    public DbEnvironment DbEnvironment;
    public Arena Arena;
    public bool IsReadOnly { get; }

    public DbSession(DbEnvironment dbEnvironment, bool readOnly = false, int arenaSize = 100_000)
    {
        Arena = new Arena(arenaSize);
        Store = new TransactionalKvStore(dbEnvironment.LightningEnvironment, dbEnvironment.ObjectDb, Arena, readOnly: readOnly);
        Cursor = Store.CreateCursor();
        DbEnvironment = dbEnvironment;
        IsReadOnly = readOnly;
    }

    public void Commit()
    {
        EnsureWritable();

        //so this will work differently in the future, right now this just commits the data to LMDB,
        //but we want to have our SaveAction on Validation logic before that.
        //so the alternative design, which may be better is to first update the readonly transaction of the TKV to the current version,
        //then execute all saveaction/validations, and only then commit

        using (var writeTransaction = DbEnvironment.LightningEnvironment.BeginTransaction())
        {
            var userId = Guid.NewGuid();
            var timestampUtc = DateTime.UtcNow;

            History.WriteCommit(Arena, DbEnvironment, writeTransaction, Store.ChangeSet!, timestampUtc, userId);

            Searcher.UpdateSearchIndex(this, writeTransaction, Store.ChangeSet!);

            Store.Commit(writeTransaction);

            Debug.Assert(writeTransaction.State == LightningTransactionState.Ready);
            var res = writeTransaction.Commit();
            Debug.Assert(res == MDBResultCode.Success, $"Expected commit to succeed, but got {res}");
        }

        Cursor.Dispose();
        Store.Dispose();

        // reset the store/cursor to a fresh read view
        Store = new TransactionalKvStore(DbEnvironment.LightningEnvironment, DbEnvironment.ObjectDb, Arena);
        Cursor = Store.CreateCursor();
    }

    /// <summary>
    /// Gets the TypId for a given Obj. If there isn't an OBJ with this objId, an empty guid is returned
    /// </summary>
    public Guid GetTypId(Guid objId)
    {
        var keyBuf = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref objId, 1));

        var resultCode = Store.Get(keyBuf, out var value);

        if (resultCode == ResultCode.Success)
        {
            return MemoryMarshal.Read<ObjValue>(value).TypId;
        }

        return Guid.Empty;
    }

    /// <summary>
    /// Deletes the Obj with the given ID and all associated fields and assoc fields.
    /// If there isn't an Obj with the given ID, this is a NoOP.
    /// </summary>
    public void DeleteObj(Guid id)
    {
        EnsureWritable();

        var prefix = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref id, 1));

        Span<byte> tempBuf = stackalloc byte[4 * 16];

        //delete everything that starts with the ObjId
        if (Cursor.SetRange(prefix) == ResultCode.Success)
        {
            do
            {
                var current = Cursor.GetCurrent();
                if (current.ResultCode != ResultCode.Success)
                    break;

                var k = current.Key;
                var v = current.Value;

                if (!k.Slice(0, 16).SequenceEqual(prefix))
                    break;

                if (v.Length > 0 && v[0] == (byte)ValueTyp.Aso)
                {
                    if (k.Length < 4 * 16)
                    {
                        Debug.Assert(false);
                        // Malformed association key (should be 4 GUIDs). Skip the opposite-delete,
                        // but still delete this entry so we can complete object deletion.
                    }
                    else
                    {
                        //flip to get other assoc
                        k.Slice(0, 2 * 16).CopyTo(tempBuf.Slice(2 * 16, 2 * 16));
                        k.Slice(16 * 2, 2 * 16).CopyTo(tempBuf.Slice(0, 2 * 16));
                        var r = Store.Delete(tempBuf);
                        Debug.Assert(r == ResultCode.Success);
                    }
                }

                var r2 = Cursor.Delete();
                Debug.Assert(r2 == ResultCode.Success);

                // Cursor.Delete() follows LMDB semantics: cursor is on a deleted position until Next().
            } while (Cursor.Next().ResultCode == ResultCode.Success);
        }
    }

    /// <summary>
    /// Creates an Obj with a given TypId and returns the ObjId of the newly created obj
    /// </summary>
    public Guid CreateObj(Guid typId, Guid fixedId = default)
    {
        EnsureWritable();

        //The idea here is to have the objects ordered by creation time in the db, in an attempt to have frequently used objects closer together,
        var id = fixedId == Guid.Empty ? Guid.CreateVersion7() : fixedId;

        var val = new ObjValue
        {
            TypId = typId,
            ValueTyp = ValueTyp.Obj
        };

        var keyBuf = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref id, 1));
        var valueBuf = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref val, 1));

        var result = Store.Put(keyBuf, valueBuf);

        if (result != ResultCode.Success)
        {
            Logging.Log(LogFlags.Error, result.ToString());
            Debug.Assert(false);
        }

        return id;
    }

    /// <summary>
    /// Creates and Association between two Objs/Flds.
    /// </summary>
    public bool CreateAso(Guid objIdA, Guid fldIdA, Guid objIdB, Guid fldIdB)
    {
        EnsureWritable();

        Span<byte> keyBuf = stackalloc byte[4 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0 * 16, 16), objIdA);
        MemoryMarshal.Write(keyBuf.Slice(1 * 16, 16), fldIdA);
        MemoryMarshal.Write(keyBuf.Slice(2 * 16, 16), objIdB);
        MemoryMarshal.Write(keyBuf.Slice(3 * 16, 16), fldIdB);
        var res1 = Store.Put(keyBuf, [(byte)ValueTyp.Aso]);

        Span<byte> otherKeyBuf = stackalloc byte[4 * 16];
        MemoryMarshal.Write(otherKeyBuf.Slice(0 * 16, 16), objIdB);
        MemoryMarshal.Write(otherKeyBuf.Slice(1 * 16, 16), fldIdB);
        MemoryMarshal.Write(otherKeyBuf.Slice(2 * 16, 16), objIdA);
        MemoryMarshal.Write(otherKeyBuf.Slice(3 * 16, 16), fldIdA);
        var res2 = Store.Put(otherKeyBuf, [(byte)ValueTyp.Aso]);

        Debug.Assert(res1 == res2);

        if (res1 == ResultCode.Success && res2 == ResultCode.Success)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Remove all assoc entries of a FLD on an OBJ
    /// </summary>
    public void RemoveAllAso(Guid objId, Guid fldId)
    {
        EnsureWritable();

        Span<byte> prefix = stackalloc byte[2 * 16];
        MemoryMarshal.Write(prefix.Slice(0 * 16, 16), objId);
        MemoryMarshal.Write(prefix.Slice(1 * 16, 16), fldId);

        Span<byte> tempBuf = stackalloc byte[4 * 16];

        //delete everything that starts with the ObjId and Aso
        if (Cursor.SetRange(prefix) == ResultCode.Success)
        {
            while (true)
            {
                var current = Cursor.GetCurrent();
                if (current.ResultCode != ResultCode.Success)
                    break;

                var k = current.Key;
                var v = current.Value;

                if (k.Length < 2 * 16 || !k.Slice(0, 2 * 16).SequenceEqual(prefix))
                    break;

                if (v.Length > 0 && v[0] == (byte)ValueTyp.Aso)
                {
                    if (k.Length < 4 * 16)
                        break;

                    //flip to get other assoc
                    k.Slice(0, 2 * 16).CopyTo(tempBuf.Slice(2 * 16, 2 * 16));
                    k.Slice(16 * 2, 2 * 16).CopyTo(tempBuf.Slice(0, 2 * 16));
                    Store.Delete(tempBuf);
                }

                Cursor.Delete();

                // Cursor.Delete() follows LMDB semantics: cursor is on a deleted position until Next().
                Cursor.Next();
            }
        }
    }

    /// <summary>
    /// Removes a specific Aso connection.
    /// <returns>True if the Aso existed; otherwise false</returns>
    /// </summary>
    public bool RemoveAso(Guid objIdA, Guid fldIdA, Guid objIdB, Guid fldIdB)
    {
        EnsureWritable();

        Span<byte> keyBuf = stackalloc byte[4 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0 * 16, 16), objIdA);
        MemoryMarshal.Write(keyBuf.Slice(1 * 16, 16), fldIdA);
        MemoryMarshal.Write(keyBuf.Slice(2 * 16, 16), objIdB);
        MemoryMarshal.Write(keyBuf.Slice(3 * 16, 16), fldIdB);
        var res1 = Store.Delete(keyBuf);

        Span<byte> otherKeyBuf = stackalloc byte[4 * 16];
        MemoryMarshal.Write(otherKeyBuf.Slice(0 * 16, 16), objIdB);
        MemoryMarshal.Write(otherKeyBuf.Slice(1 * 16, 16), fldIdB);
        MemoryMarshal.Write(otherKeyBuf.Slice(2 * 16, 16), objIdA);
        MemoryMarshal.Write(otherKeyBuf.Slice(3 * 16, 16), fldIdA);
        var res2 = Store.Delete(otherKeyBuf);

        Debug.Assert(res1 == res2);

        if (res1 == ResultCode.Success && res2 == ResultCode.Success)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Sets the value of a field
    /// </summary>
    public void SetFldValue(Guid objId, Guid fldId, ReadOnlySpan<byte> span)
    {
        EnsureWritable();

        //todo, in debug mode we could check if the obj exists!


        Span<byte> keyBuf = stackalloc byte[2 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0 * 16, 16), objId);
        MemoryMarshal.Write(keyBuf.Slice(1 * 16, 16), fldId);

        if (span.Length == 0)
        {
            Store.Delete(keyBuf);
        }
        else
        {
            Span<byte> valueBuf = stackalloc byte[1 + span.Length]; //todo ensure span.length is not too large, should use arena here....
            valueBuf[0] = (byte)ValueTyp.Val;
            span.CopyTo(valueBuf.Slice(1));

            var r = Store.Put(keyBuf, valueBuf);
            Debug.Assert(ResultCode.Success == r);
        }
    }

    /// <summary>
    /// Gets a field value as a span. The returned span is valid as long as the session persists.
    /// </summary>
    public ReadOnlySpan<byte> GetFldValue(Guid objId, Guid fldId)
    {
        Span<byte> keyBuf = stackalloc byte[2 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0 * 16, 16), objId);
        MemoryMarshal.Write(keyBuf.Slice(1 * 16, 16), fldId);

        var code = Store.Get(keyBuf, out var value);
        if (code != ResultCode.Success || value.Length == 0)
            return [];

        // Stored as: [ValueTyp][payload]
        if (value[0] != (byte)ValueTyp.Val)
            return [];

        //hmm, maybe we can avoid this copy, but maybe not
        return Arena.AllocateSlice(value.Slice(1));
    }

    /// <summary>
    /// Gets the Obj of an Aso. Returns null if the Aso is empty
    /// </summary>
    public Guid? GetSingleAsoValue(Guid objId, Guid fldId)
    {
        //todo avoid overhead of enumerating aso, just manually do a get call!!!

        using var enumerator = EnumerateAso(objId, fldId).GetEnumerator();

        var hasValue = enumerator.MoveNext();
        if (!hasValue)
            return null;

        return enumerator.Current.ObjId;
    }

    /// <summary>
    /// Enumerates the
    /// </summary>
    public AsoFldEnumerable EnumerateAso(Guid objId, Guid fldId)
    {
        //todo we can reuse the cursor, the problem is once the user starts to enumerate multiple asos at the same time,
        //so we would need to detect that and at that point create a new cursor, or even better we have a cursor pool,
        //where for each enumeration we look if if have a non used cursor, once the enumeration finishes,
        //the cursor gets returned to the pool
        var cursor = Store.CreateCursor();

        //todo, should we allow the modification of the Aso (add/remove) of entries while we are enumerating?
        //if yes: we need to clearly specify the behaviour
        //if no: throw an exception if it happens
        return new AsoFldEnumerable(cursor, objId, fldId);
    }

    /// <summary>
    /// Gets the number of connections in an AsoFld
    /// </summary>
    public int GetAsoCount(Guid objId, Guid fldId)
    {
        //todo, improve performance by storing the count in the db
        int count = 0;
        foreach (var _ in EnumerateAso(objId, fldId))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Disposes the underlying KvStore and cached cursor
    /// </summary>
    public void Dispose()
    {
        Cursor.Dispose();
        Store.Dispose();
    }

    private void EnsureWritable()
    {
        if (IsReadOnly)
            throw new InvalidOperationException("DbSession is read-only");
    }


    //Todo this method should be replaced by the searching system...
    public IEnumerable<(Guid objId, Guid typId)> EnumerateObjs()
    {
        //todo write manual enumerator that doesn't allocate!!!
        var result = Cursor.SetRange([0]);
        if (result == ResultCode.Success)
        {
            do
            {
                var (_, currentKey, currentValue) = Cursor.GetCurrent();

                if (currentKey.Length == 16 && currentValue.Length > 0 && currentValue[0] == (byte)ValueTyp.Obj)
                {
                    // Obj keys are exactly 16 bytes (ObjId). Field/assoc keys are longer.
                    // Validate the value layout too: [ValueTyp][Guid TypId] => 17 bytes.
                    if (currentValue.Length == Unsafe.SizeOf<ObjValue>())
                        yield return (MemoryMarshal.Read<Guid>(currentKey), MemoryMarshal.Read<ObjValue>(currentValue).TypId);
                }
            } while (Cursor.Next().ResultCode == ResultCode.Success);
        }
    }

    public T? GetObjFromGuid<T>(Guid objId) where T : struct, ITransactionObject
    {
        if (TryGetObjFromGuid<T>(objId, out var val))
        {
            return val;
        }

        //Logging.Log(LogFlags.Error, $"Can't find {typeof(T).Name}({T.TypId}) with id {objId}");
        return null;
    }

    public bool TryGetObjFromGuid<T>(Guid objId, out T val) where T : ITransactionObject, new()
    {
        val = default!;

        if (GetTypId(objId) == T.TypId) //todo handle inheritance
        {
            val = new T
            {
                ObjId = objId,
                DbSession = this
            };
            return true;
        }

        return false;
    }
}

public struct AsoEnumeratorObj
{
    public Guid ObjId;
    public Guid FldId;
}

public struct AsoFldEnumerable : IEnumerable<AsoEnumeratorObj>
{
    private readonly TransactionalKvStore.Cursor _cursor;
    private readonly Guid _objId;
    private readonly Guid _fldId;

    public AsoFldEnumerable(TransactionalKvStore.Cursor cursor, Guid objId, Guid fldId)
    {
        _cursor = cursor;
        _objId = objId;
        _fldId = fldId;
    }

    public AsoFldEnumerator GetEnumerator()
    {
        return new AsoFldEnumerator(_cursor, _objId, _fldId);
    }

    IEnumerator<AsoEnumeratorObj> IEnumerable<AsoEnumeratorObj>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public struct AsoFldEnumerator : IEnumerator<AsoEnumeratorObj>
{
    private AsoEnumeratorObj _current1;
    private TransactionalKvStore.Cursor cursor;
    private bool isFirst;
    private Guid objId;
    private Guid fldId;

    public AsoFldEnumerator(TransactionalKvStore.Cursor cursor, Guid objId, Guid fldId)
    {
        isFirst = true;
        this.cursor = cursor;
        this.objId = objId;
        this.fldId = fldId;
    }

    public void Dispose()
    {
        cursor.Dispose();
    }

    public bool MoveNext()
    {
        ResultCode code;
        ReadOnlySpan<byte> key;

        Span<byte> prefixKeyBuf = stackalloc byte[2 * 16];
        MemoryMarshal.Write(prefixKeyBuf.Slice(0 * 16, 16), objId);
        MemoryMarshal.Write(prefixKeyBuf.Slice(1 * 16, 16), fldId);

        if (isFirst)
        {
            code = cursor.SetRange(prefixKeyBuf);
            if (code != ResultCode.Success)
                return false;

            var current = cursor.GetCurrent();
            if (current.ResultCode != ResultCode.Success)
                return false;

            key = current.Key;
            isFirst = false;
        }
        else
        {
            var next = cursor.Next();
            code = next.ResultCode;
            key = next.Key;
        }

        if (code != ResultCode.Success)
            return false;

        if (key.Length < 2 * 16 || !key.Slice(0, 2 * 16).SequenceEqual(prefixKeyBuf))
            return false;

        _current1.ObjId = MemoryMarshal.Read<Guid>(key.Slice(2 * 16, 16));
        _current1.FldId = MemoryMarshal.Read<Guid>(key.Slice(3 * 16, 16));

        return true;
    }

    public void Reset()
    {
        throw new NotImplementedException();
    }

    public AsoEnumeratorObj Current => _current1;

    AsoEnumeratorObj IEnumerator<AsoEnumeratorObj>.Current => _current1;

    object IEnumerator.Current => throw new NotImplementedException();
}

public enum ValueTyp : byte
{
    Obj = 0,
    Aso = 1,
    Val = 2,
}

public enum ChangeType : byte
{
    ObjCreated = 0,
    ObjDeleted,
    AsoAdded,
    AsoRemoved,
    FldChanged
}

[StructLayout(LayoutKind.Explicit)]
public struct ObjValue
{
    [FieldOffset(0)] public ValueTyp ValueTyp;

    [FieldOffset(1)] public Guid TypId;
}

public enum ResultCode
{
    Success,
    NotFound
}

public enum ValueFlag : byte
{
    AddModify,
    Delete
}
