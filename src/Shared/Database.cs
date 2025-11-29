using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LightningDB;

namespace Shared;

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


//do we want/need locks in here to ensure that there aren't multiple threads at the same time interacting with the transaction
public sealed class Transaction : IDisposable
{
    public readonly LightningTransaction LightningTransaction;
    public readonly LightningCursor Cursor;
    public readonly LightningDatabase ObjectDb;
    public readonly LightningDatabase HistoryDb;

    public Transaction(Environment environment)
    {
        LightningTransaction = environment.LightningEnvironment.BeginTransaction();
        Cursor = LightningTransaction.CreateCursor(environment.ObjectDb);
        ObjectDb = environment.ObjectDb;
        HistoryDb = environment.HistoryDb;
    }

    public void Commit()
    {
        LightningTransaction.Commit();
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
        //but I'm not sure if this will actually work
        var id = Guid.CreateVersion7();

        Span<byte> keyBuf = stackalloc byte[2 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0, 16), id);
        MemoryMarshal.Write(keyBuf.Slice(16, 16), typId);

        var result = LightningTransaction.Put(ObjectDb, keyBuf, [(byte)ValueTyp.Obj]);
        Debug.Assert(result == MDBResultCode.Success);

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