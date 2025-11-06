using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using LightningDB;

namespace TheProject;

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

public class Transaction : IDisposable
{
    public static DatabaseConfiguration CreateConfiguration = new()
    {
        Flags = DatabaseOpenFlags.Create
    };

    public LightningTransaction LightningTransaction;
    public LightningDatabase Database;
    public LightningCursor Cursor;

    public Transaction(LightningEnvironment env)
    {
        LightningTransaction = env.BeginTransaction();
        Database = LightningTransaction.OpenDatabase(configuration: CreateConfiguration);
        Cursor = LightningTransaction.CreateCursor(Database);
    }

    public void Commit()
    {
        LightningTransaction.Commit();
    }

    public void DebugPrintAllValues()
    {
        Console.WriteLine("DB Content:");

        var result = Cursor.SetRange([0]);
        if (result == MDBResultCode.Success)
        {
            do
            {
                var current = Cursor.GetCurrent();
                var currentKey = current.key.AsSpan();
                var currentValue = current.value.AsSpan();

                for (int i = 0; i < currentKey.Length / 16; i++)
                {
                    Console.Write(MemoryMarshal.Read<Guid>(currentKey.Slice(i * 16, 16)) + ", ");
                }

                Console.Write(":");

                Console.WriteLine((ValueTyp)currentValue[0]);
            }
            // Move to the next duplicate value
            while (Cursor.Next().resultCode == MDBResultCode.Success);
        }
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
                    LightningTransaction.Delete(Database, tempBuf);
                }

                Cursor.Delete();
            } while (Cursor.Next().resultCode == MDBResultCode.Success);
        }
    }

    public Guid CreateObj(Guid typId)
    {
        var id = Guid.NewGuid();

        Span<byte> keyBuf = stackalloc byte[2 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0, 16), id);
        MemoryMarshal.Write(keyBuf.Slice(16, 16), typId);

        LightningTransaction.Put(Database, keyBuf, [(byte)ValueTyp.Obj]);

        return id;
    }

    public void CreateAso(Guid objIdA, Guid fldIdA, Guid objIdB, Guid fldIdB)
    {
        Span<byte> keyBuf = stackalloc byte[4 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0*16, 16), objIdA);
        MemoryMarshal.Write(keyBuf.Slice(1*16, 16), fldIdA);
        MemoryMarshal.Write(keyBuf.Slice(2*16, 16), objIdB);
        MemoryMarshal.Write(keyBuf.Slice(3*16, 16), fldIdB);
        LightningTransaction.Put(Database, keyBuf, [(byte)ValueTyp.Aso]);

        Span<byte> otherKeyBuf = stackalloc byte[4 * 16];
        MemoryMarshal.Write(otherKeyBuf.Slice(0*16, 16), objIdB);
        MemoryMarshal.Write(otherKeyBuf.Slice(1*16, 16), fldIdB);
        MemoryMarshal.Write(otherKeyBuf.Slice(2*16, 16), objIdA);
        MemoryMarshal.Write(otherKeyBuf.Slice(3*16, 16), fldIdA);
        LightningTransaction.Put(Database, otherKeyBuf, [(byte)ValueTyp.Aso]);
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
                    LightningTransaction.Delete(Database, tempBuf);
                }

                Cursor.Delete();
            } while (Cursor.Next().resultCode == MDBResultCode.Success);
        }
    }

    public void RemoveAso(Guid objIdA, Guid fldIdA, Guid objIdB, Guid fldIdB)
    {
        Span<byte> keyBuf = stackalloc byte[4 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0*16, 16), objIdA);
        MemoryMarshal.Write(keyBuf.Slice(1*16, 16), fldIdA);
        MemoryMarshal.Write(keyBuf.Slice(2*16, 16), objIdB);
        MemoryMarshal.Write(keyBuf.Slice(3*16, 16), fldIdB);
        LightningTransaction.Delete(Database, keyBuf);

        Span<byte> otherKeyBuf = stackalloc byte[4 * 16];
        MemoryMarshal.Write(otherKeyBuf.Slice(0*16, 16), objIdB);
        MemoryMarshal.Write(otherKeyBuf.Slice(1*16, 16), fldIdB);
        MemoryMarshal.Write(otherKeyBuf.Slice(2*16, 16), objIdA);
        MemoryMarshal.Write(otherKeyBuf.Slice(3*16, 16), fldIdA);
        LightningTransaction.Delete(Database, otherKeyBuf);
    }

    public void SetFldValue(Guid objId, Guid fldId, Slice<byte> span)
    {
        Span<byte> keyBuf = stackalloc byte[2 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0*16, 16), objId);
        MemoryMarshal.Write(keyBuf.Slice(1*16, 16), fldId);

        if (span.Length == 0)
        {
            LightningTransaction.Delete(Database, keyBuf);
        }
        else
        {
            Span<byte> valueBuf = stackalloc byte[1 + span.Length]; //todo ensure span.length is not too large, should use arena here....
            valueBuf[0] = (byte)ValueTyp.Val;
            span.AsSpan().CopyTo(valueBuf.Slice(1));

            LightningTransaction.Put(Database, keyBuf, valueBuf);
        }
    }

    public unsafe Slice<byte> GetFldValue(Guid objId, Guid fldId)
    {
        Span<byte> keyBuf = stackalloc byte[2 * 16];
        MemoryMarshal.Write(keyBuf.Slice(0*16, 16), objId);
        MemoryMarshal.Write(keyBuf.Slice(1*16, 16), fldId);

        var (s, k, v) = LightningTransaction.Get(Database, keyBuf);

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
        var cursor = LightningTransaction.CreateCursor(Database);

        return new AsoFldEnumerable(cursor, objId, fldId);
    }

    public void Dispose()
    {
        Cursor.Dispose();
        LightningTransaction.Dispose();
        Database.Dispose();
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