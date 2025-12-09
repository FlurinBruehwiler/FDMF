using System.Collections;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LightningDB;

namespace Shared.Database;

public unsafe struct Slice<T> where T : unmanaged
{
    public Slice(ReadOnlySpan<T> span)
    {
        fixed (T* ptr = span)
        {
            Items = ptr;
            Length = span.Length;
        }
    }

    public Slice(T* items, int length)
    {
        Items = items;
        Length = length;
    }

    public T* Items;
    public int Length;

    public Span<T> AsSpan()
    {
        return new Span<T>(Items, Length);
    }

    public Slice<byte> AsByteSlice()
    {
        return new Slice<byte>((byte*)Items, Length * sizeof(T));
    }

    public static Slice<T> Empty()
    {
        return new Slice<T>();
    }
}

public static class Extensions
{
    public static Slice<T> AsSlice<T>(this ReadOnlySpan<T> span) where T : unmanaged
    {
        return new Slice<T>(span);
    }
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
    [FieldOffset(0)]
    public ValueTyp ValueTyp;

    [FieldOffset(1)]
    public Guid TypId;
}

public struct Change
{
    public ChangeType ChangeType;
    public DateTime DateTime;
    public Guid UserId;

    public Guid ObjAId;
    public Guid FldAId;
    public Guid ObjBId;
    public Guid FldBId;
}

// HistDb
// histId = ChangeInfo(Created, ObjId, DateTime, User)

// HistDb_ObjKey
// ObjId = histId

// HistDb_UserKey
// UserId = histId

public enum ResultCode
{
    Success,
    NotFound
}

public enum ValueFlag : byte{
    AddModify,
    Delete
}

//This represents a "long lived" transaction, there can multiple of these write transactions at the same time.
//Specifically, it reads from the LMDB DB with a changeset layered ontop.
//Once Commit is called, a LMDB write transaction is created (behind a lock) and the changeset is written to LMDB.
//This replicates the LMDB API, we could put it behind an interface so that the three KV stores (LMDB, ChangeSet, LMDB + Changeset) have the same api.
public sealed class TransactionalKvStore
{
    public required LightningTransaction ReadTransaction;
    public required LightningDatabase Database;

    //todo we should combine these changesets, the problem is, we need a flag to indicate if a value was deleted or not,
    //if we have the flag in the value, we have to copy the value around (to insert the flag at the beginning),
    //so we could also just append it to the key, which is usually smaller....
    public readonly BPlusTree ChangeSet = new();

    public ResultCode Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        //remove from deleteChangeSet if exists
        // DeleteChangeSet.Delete(key.ToArray());

        //todo, don't call ToArray
        return ChangeSet.Put(key.ToArray(), WrapValue(value.ToArray(), ValueFlag.AddModify));
    }

    private static byte[] WrapValue(ReadOnlySpan<byte> data, ValueFlag flag)
    {
        var arr = new byte[data.Length + 1];
        arr[0] = (byte)flag;
        data.CopyTo(arr.AsSpan(1));
        return arr;
    }

    public (ResultCode resultCode, Slice<byte> key, byte[] value) Get(ReadOnlySpan<byte> key)
    {
        //check the deleted changeset
        //check the additive changeset
        //check the lmdb transaction

        //todo don't call ToArray
        var (additiveResult, _, newValue) = ChangeSet.Get(key);
        if (additiveResult == ResultCode.Success)
        {
            if(newValue[0] == (byte)ValueFlag.Delete)
                return (ResultCode.NotFound, Slice<byte>.Empty(), []);

            return (ResultCode.Success, new Slice<byte>(key), newValue.Slice(1).ToArray());
        }

        var (lmdbResult, _, value) = ReadTransaction.Get(Database, key);
        if (lmdbResult == MDBResultCode.Success)
            return (ResultCode.Success, new Slice<byte>(key), value.CopyToNewArray()); //todo don't create array

        return (ResultCode.NotFound, Slice<byte>.Empty(), []);
    }

    public ResultCode Delete(ReadOnlySpan<byte> key)
    {
        if (ReadTransaction.Get(Database, key).resultCode == MDBResultCode.Success)
        {
            ChangeSet.Put(key.ToArray(), [ (byte)ValueFlag.Delete ]);
        }
        else
        {
            ChangeSet.Delete(key.ToArray());
        }

        return ResultCode.Success; //todo should we check if it existed
    }

    public Cursor CreateCursor()
    {
        return new Cursor
        {
            LightningCursor = ReadTransaction.CreateCursor(Database),
            ChangeSetCursor = ChangeSet.CreateCursor(),
            ChangeSet = ChangeSet
        };
    }

    public class Cursor
    {
        public required LightningCursor LightningCursor;
        public required BPlusTree.Cursor ChangeSetCursor;

        public bool BaseIsFinished;
        public bool ChangeIsFinished;

        public required BPlusTree ChangeSet;

        public ResultCode SetRange(ReadOnlySpan<byte> key)
        {
            BaseIsFinished = LightningCursor.SetRange(key) == MDBResultCode.NotFound;
            ChangeIsFinished = ChangeSetCursor.SetRange(key.ToArray()) == ResultCode.NotFound;
            //DeleteCursor.SetRange(key.ToArray());

            return ResultCode.Success;
        }

        public ResultCode Delete()
        {
            var baseSet = LightningCursor.GetCurrent();
            var changeSet = ChangeSetCursor.GetCurrent();

            var comp = BPlusTree.CompareSpan(baseSet.key.AsSpan(), changeSet.key.AsSpan());
            if (comp <= 0)
            {
                ChangeSet.Put(baseSet.key.CopyToNewArray(), []);
            }
            else
            {
                //change is smaller than base
                ChangeSetCursor.Delete();
            }

            return ResultCode.Success;
        }

        public (ResultCode resultCode, byte[] key, byte[] value) GetCurrent()
        {
            var baseSet = LightningCursor.GetCurrent();
            var changeSet = ChangeSetCursor.GetCurrent();

            //return the lower, if both are the same, return changeSet

            var comp = BPlusTree.CompareSpan(baseSet.key.AsSpan(), changeSet.key.AsSpan());
            if (ChangeIsFinished || (comp < 0 && !BaseIsFinished))
            {
                 // baseSet is smaller than changeSet
                return (ResultCode.Success, baseSet.key.CopyToNewArray(), baseSet.value.CopyToNewArray());
            }

            Debug.Assert(changeSet.value[0] == (byte)ValueFlag.AddModify);

            return (ResultCode.Success, changeSet.key, changeSet.value.AsSpan(1).ToArray());
        }

        public (ResultCode resultCode, byte[] key, byte[] value) Next()
        {
            //we are currently on a value, it is either from the read or the change dataset
            //both read and change have a current value
            //we take the lower of the two and call next

            //if both are the same, we advance both. GetCurrent returns the lower value!

            next:
            var a = LightningCursor.GetCurrent();
            var b = ChangeSetCursor.GetCurrent();

            var comp = BPlusTree.CompareSpan(a.key.AsSpan(), b.key.AsSpan());
            if (!BaseIsFinished && comp <= 0)
            {
                BaseIsFinished = LightningCursor.Next().resultCode == MDBResultCode.NotFound;
            }
            else if (comp >= 0)
            {
                var (result, _, value) = ChangeSetCursor.Next();

                ChangeIsFinished = result == ResultCode.NotFound;

                if (result == ResultCode.Success)
                {
                    if (value[0] == (byte)ValueFlag.Delete)
                    {
                        goto next;
                    }
                }

                goto end;
            }

            if (BaseIsFinished)
            {
                var (result, _, value) = ChangeSetCursor.Next();

                ChangeIsFinished = result == ResultCode.NotFound;

                if (result == ResultCode.Success)
                {
                    if (value[0] == (byte)ValueFlag.Delete)
                    {
                        goto next;
                    }
                }
            }

            end:
            if (ChangeIsFinished && BaseIsFinished)
                return (ResultCode.NotFound, [], []);

            return GetCurrent();
        }
    }
}

//do we want/need locks in here to ensure that there aren't multiple threads at the same time interacting with the transaction
public sealed class Transaction : IDisposable
{
    public readonly LightningTransaction LightningTransaction;
    public readonly LightningCursor Cursor;
    public readonly LightningDatabase ObjectDb;
    public readonly LightningDatabase HistoryDb;

    public Transaction(Environment environment)
    {
        LightningTransaction = environment.LightningEnvironment.BeginTransaction(TransactionBeginFlags.ReadOnly);

        Cursor = LightningTransaction.CreateCursor(environment.ObjectDb);
        ObjectDb = environment.ObjectDb;
        HistoryDb = environment.HistoryDb;
    }

    public void Commit()
    {
        LightningTransaction.Commit();
    }

    public Guid GetTypId(Guid objId)
    {
        var keyBuf = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref objId, 1));

        var (resultCode, key, value) = LightningTransaction.Get(ObjectDb, keyBuf);

        if (resultCode == MDBResultCode.Success)
        {
            return MemoryMarshal.Read<ObjValue>(value.AsSpan()).TypId;
        }

        return Guid.Empty;
    }

    public void DeleteObj(Guid id)
    {
        var prefix = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref id, 1));

        Span<byte> tempBuf = stackalloc byte[4 * 16];

        //delete everything that starts with the ObjId
        if (Cursor.SetRange(prefix) == MDBResultCode.Success)
        {
            do
            {
                var (_, k, v) = Cursor.GetCurrent();

                if(!k.AsSpan().Slice(0, 16).SequenceEqual(prefix))
                    break;

                if (v.AsSpan()[0] == (byte)ValueTyp.Aso)
                {
                    //flip to get other assoc
                    k.AsSpan().Slice(0, 2 * 16).CopyTo(tempBuf.Slice(2 * 16, 2 * 16));
                    k.AsSpan().Slice(16 * 2, 2 * 16).CopyTo(tempBuf.Slice(0, 2 * 16));
                    LightningTransaction.Delete(ObjectDb, tempBuf);
                }

                Cursor.Delete();
            } while (Cursor.Next().resultCode == MDBResultCode.Success);
        }

        AddHistoryEntry(new Change
        {
            ChangeType = ChangeType.ObjDeleted,
            DateTime = DateTime.UtcNow,
            UserId = Guid.Empty,
            ObjAId = id
        });
    }

    private void AddHistoryEntry(Change change, Slice<byte> newFldData = default)
    {
        change.DateTime = DateTime.UtcNow;
        change.UserId = Guid.Empty;//todo

        var guidKey = Guid.CreateVersion7();
        var key = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref guidKey, 1));
        var value = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref change, 1));

        //todo write field data to the history
        LightningTransaction.Put(HistoryDb, key, value);
    }

    public Guid CreateObj(Guid typId)
    {
        //The idea here is to have the objects ordered by creation time in the db, in an attempt to have frequently used objects closer together,
        var id = Guid.CreateVersion7();

        var val = new ObjValue
        {
            TypId = typId,
            ValueTyp = ValueTyp.Obj
        };

        var keyBuf = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref id, 1));
        var valueBuf = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref val, 1));

        var result = LightningTransaction.Put(ObjectDb, keyBuf, valueBuf);

        if (result != MDBResultCode.Success)
        {
            Console.WriteLine(result);
            Debug.Assert(false);
        }

        AddHistoryEntry(new Change
        {
            ChangeType = ChangeType.ObjCreated,
            ObjAId = id
        });

        return id;
    }

    public bool CreateAso(Guid objIdA, Guid fldIdA, Guid objIdB, Guid fldIdB)
    {
        Span<byte> keyBuf = stackalloc byte[4 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0*16, 16), objIdA);
        MemoryMarshal.Write(keyBuf.Slice(1*16, 16), fldIdA);
        MemoryMarshal.Write(keyBuf.Slice(2*16, 16), objIdB);
        MemoryMarshal.Write(keyBuf.Slice(3*16, 16), fldIdB);
        var res1 = LightningTransaction.Put(ObjectDb, keyBuf, [(byte)ValueTyp.Aso]);

        Span<byte> otherKeyBuf = stackalloc byte[4 * 16];
        MemoryMarshal.Write(otherKeyBuf.Slice(0*16, 16), objIdB);
        MemoryMarshal.Write(otherKeyBuf.Slice(1*16, 16), fldIdB);
        MemoryMarshal.Write(otherKeyBuf.Slice(2*16, 16), objIdA);
        MemoryMarshal.Write(otherKeyBuf.Slice(3*16, 16), fldIdA);
        var res2 = LightningTransaction.Put(ObjectDb, otherKeyBuf, [(byte)ValueTyp.Aso]);

        Debug.Assert(res1 == res2);

        if (res1 == MDBResultCode.Success && res2 == MDBResultCode.Success)
        {
            AddHistoryEntry(new Change
            {
                ChangeType = ChangeType.AsoAdded,
                ObjAId = objIdA,
                FldAId = fldIdA,
                ObjBId = objIdB,
                FldBId = fldIdB
            });
            return true;
        }

        return false;
    }

    public void RemoveAllAso(Guid objId, Guid fldId)
    {
        Span<byte> prefix = stackalloc byte[2 * 16];
        MemoryMarshal.Write(prefix.Slice(0*16, 16), objId);
        MemoryMarshal.Write(prefix.Slice(1*16, 16), fldId);


        Span<byte> tempBuf = stackalloc byte[4 * 16];

        //delete everything that starts with the ObjId and Aso
        if (Cursor.SetRange(prefix) == MDBResultCode.Success)
        {
            do
            {
                var (_, k, v) = Cursor.GetCurrent();

                if(!k.AsSpan().Slice(0, 2*16).SequenceEqual(prefix))
                    break;

                if (v.AsSpan()[0] == (byte)ValueTyp.Aso)
                {
                    //flip to get other assoc
                    k.AsSpan().Slice(0, 2 * 16).CopyTo(tempBuf.Slice(2 * 16, 2 * 16));
                    k.AsSpan().Slice(16 * 2, 2 * 16).CopyTo(tempBuf.Slice(0, 2 * 16));
                    LightningTransaction.Delete(ObjectDb, tempBuf);
                }

                var otherObjId = MemoryMarshal.Read<Guid>(k.AsSpan().Slice(16 * 2, 16 * 1));
                var otherFldId = MemoryMarshal.Read<Guid>(k.AsSpan().Slice(16 * 3, 16 * 1));

                AddHistoryEntry(new Change
                {
                    ChangeType = ChangeType.AsoRemoved,
                    ObjAId = objId,
                    FldAId = fldId,
                    ObjBId = otherObjId,
                    FldBId = otherFldId
                });
                Cursor.Delete();
            } while (Cursor.Next().resultCode == MDBResultCode.Success);
        }
    }

    public bool RemoveAso(Guid objIdA, Guid fldIdA, Guid objIdB, Guid fldIdB)
    {
        Span<byte> keyBuf = stackalloc byte[4 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0*16, 16), objIdA);
        MemoryMarshal.Write(keyBuf.Slice(1*16, 16), fldIdA);
        MemoryMarshal.Write(keyBuf.Slice(2*16, 16), objIdB);
        MemoryMarshal.Write(keyBuf.Slice(3*16, 16), fldIdB);
        var res1 = LightningTransaction.Delete(ObjectDb, keyBuf);

        Span<byte> otherKeyBuf = stackalloc byte[4 * 16];
        MemoryMarshal.Write(otherKeyBuf.Slice(0*16, 16), objIdB);
        MemoryMarshal.Write(otherKeyBuf.Slice(1*16, 16), fldIdB);
        MemoryMarshal.Write(otherKeyBuf.Slice(2*16, 16), objIdA);
        MemoryMarshal.Write(otherKeyBuf.Slice(3*16, 16), fldIdA);
        var res2 = LightningTransaction.Delete(ObjectDb, otherKeyBuf);

        Debug.Assert(res1 == res2);

        if (res1 == MDBResultCode.Success && res2 == MDBResultCode.Success)
        {
            AddHistoryEntry(new Change
            {
                ChangeType = ChangeType.AsoRemoved,
                ObjAId = objIdA,
                FldAId = fldIdA,
                ObjBId = objIdB,
                FldBId = fldIdB
            });
            return true;
        }

        return false;
    }

    public void SetFldValue(Guid objId, Guid fldId, Slice<byte> span)
    {
        Span<byte> keyBuf = stackalloc byte[2 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0*16, 16), objId);
        MemoryMarshal.Write(keyBuf.Slice(1*16, 16), fldId);

        if (span.Length == 0)
        {
            LightningTransaction.Delete(ObjectDb, keyBuf);
        }
        else
        {
            Span<byte> valueBuf = stackalloc byte[1 + span.Length]; //todo ensure span.length is not too large, should use arena here....
            valueBuf[0] = (byte)ValueTyp.Val;
            span.AsSpan().CopyTo(valueBuf.Slice(1));

            LightningTransaction.Put(ObjectDb, keyBuf, valueBuf);
        }

        //todo this should only be changed if the value is actually different
        AddHistoryEntry(new Change
        {
            ChangeType = ChangeType.FldChanged,
            ObjAId = objId,
            FldAId = fldId
        }, span);
    }

    public unsafe Slice<byte> GetFldValue(Guid objId, Guid fldId)
    {
        Span<byte> keyBuf = stackalloc byte[2 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0*16, 16), objId);
        MemoryMarshal.Write(keyBuf.Slice(1*16, 16), fldId);

        var (s, k, v) = LightningTransaction.Get(ObjectDb, keyBuf);

        if (s != MDBResultCode.Success) //todo logging
            return default;

        return new Slice<byte>(v.AsSpan().Slice(1));
    }

    //todo avoid overhead of enumerating aso
    public Guid? GetSingleAsoValue(Guid objId, Guid fldId)
    {
        using var enumerator = EnumerateAso(objId, fldId).GetEnumerator();

        var hasValue = enumerator.MoveNext();
        if (!hasValue)
            return null;

        return enumerator.Current.ObjId;
    }

    public AsoFldEnumerable EnumerateAso(Guid objId, Guid fldId)
    {
        //todo we can reuse the cursor, the problem is once the user starts to enumerate multiple asos at the same time,
        //so we would need to detect that and at that point create a new cursor, or even better we have a cursor pool,
        //where for each enumeration we look if if have a non used cursor, once the enumeration finishes,
        //the cursor gets returned to the pool
        var cursor = LightningTransaction.CreateCursor(ObjectDb);

        return new AsoFldEnumerable(cursor, objId, fldId);
    }

    //todo write manual enumerator that doesn't allocate!!!
    public IEnumerable<(Guid objId, Guid typId)> EnumerateObjs()
    {
        var result = Cursor.SetRange([0]);
        if (result == MDBResultCode.Success)
        {
            do
            {
                var current = Cursor.GetCurrent();
                var currentKey = current.key.AsSpan();
                var currentValue = current.value.AsSpan();

                if (currentValue[0] == (byte)ValueTyp.Obj)
                {
                    yield return (MemoryMarshal.Read<Guid>(currentKey), MemoryMarshal.Read<ObjValue>(currentValue).TypId);
                }
            }
            while (Cursor.Next().resultCode == MDBResultCode.Success);
        }
    }

    public void Dispose()
    {
        Cursor.Dispose();
        LightningTransaction.Dispose();
        ObjectDb.Dispose();
    }

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

    public void DebugPrintAllValues()
    {
        Logging.Log(LogFlags.Info, "DB Content:");

        var result = Cursor.SetRange([0]);
        if (result == MDBResultCode.Success)
        {
            do
            {
                var current = Cursor.GetCurrent();
                var currentKey = current.key.AsSpan();
                var currentValue = current.value.AsSpan();

                throw new NotImplementedException();
                // for (int i = 0; i < currentKey.Length / 16; i++)
                // {
                //     Logging.Log(LogFlags.Info, MemoryMarshal.Read<Guid>(currentKey.Slice(i * 16, 16)) + ", ");
                // }
                //
                // Logging.Log(LogFlags.Info, ":");
                //
                // Logging.Log(LogFlags.Info, (ValueTyp)currentValue[0]);
            }
            // Move to the next duplicate value
            while (Cursor.Next().resultCode == MDBResultCode.Success);
        }
    }
}

public struct AsoEnumeratorObj
{
    public Guid ObjId;
    public Guid FldId;
}

public struct AsoFldEnumerable : IEnumerable<AsoEnumeratorObj>
{
    private readonly LightningCursor _cursor;
    private readonly Guid _objId;
    private readonly Guid _fldId;

    public AsoFldEnumerable(LightningCursor cursor, Guid objId, Guid fldId)
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
    private LightningCursor cursor;
    private bool isFirst;
    private Guid objId;
    private Guid fldId;

    public AsoFldEnumerator(LightningCursor cursor, Guid objId, Guid fldId)
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
        MDBResultCode code;
        MDBValue key;

        Span<byte> prefixKeyBuf = stackalloc byte[2 * 16];
        MemoryMarshal.Write(prefixKeyBuf.Slice(0*16, 16), objId);
        MemoryMarshal.Write(prefixKeyBuf.Slice(1*16, 16), fldId);

        if (isFirst)
        {
            code = cursor.SetRange(prefixKeyBuf);
            if (code != MDBResultCode.Success)
                return false;

            (_, key, _) = cursor.GetCurrent();
            isFirst = false;
        }
        else
        {
            (code, key, _) = cursor.Next();
        }

        if (code != MDBResultCode.Success)
            return false;

        if (!key.AsSpan().Slice(0, 2 * 16).SequenceEqual(prefixKeyBuf))
            return false;

        _current1.ObjId = MemoryMarshal.Read<Guid>(key.AsSpan().Slice(2 * 16, 16));
        _current1.FldId = MemoryMarshal.Read<Guid>(key.AsSpan().Slice(3 * 16, 16));

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

public static class Helper
{
    public static bool MemoryEquals<T>(T val, T other) where T : unmanaged
    {
        ReadOnlySpan<byte> value = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref val, 1));

        ReadOnlySpan<byte> otherValue = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref other, 1));

        return value.SequenceEqual(otherValue);
    }

    public static void FireAndForget(Task t)
    {
        t.ContinueWith(x =>
        {
            Logging.LogException(x.Exception);
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}

/*
 * 50
 * 50 10
 * 60
 * 61
 *
 *
 */

//Keys:
//OBJ: ObjId, TypId
//ASO: ObjIdA, FldIdA, ObjIdB, FldIdB
//ASO: ObjIdB, FldIdB, ObjIdA, FldIdA
//VAL: ObjId, FldId

[StructLayout(LayoutKind.Explicit)]
public struct FldValue
{
    [FieldOffset(0)]
    public long Integer;

    [FieldOffset(0)]
    public DateTime DateTime;

    [FieldOffset(0)]
    public bool Bool;

    [FieldOffset(0)]
    public decimal Decimal;
}

[InlineArray(16)]
public struct InlineData
{
    public byte Data;
}

public enum ValueTyp : byte
{
    Obj = 0,
    Aso = 1,
    Val = 2,
}