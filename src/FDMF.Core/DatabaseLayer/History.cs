using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FDMF.Core.Utils;
using LightningDB;

namespace FDMF.Core.DatabaseLayer;

public enum HistoryEventType : byte
{
    ObjCreated = 0,
    ObjDeleted = 1,
    FldChanged = 2,
    AsoAdded = 3,
    AsoRemoved = 4,
}

public readonly struct HistoryEvent
{
    public readonly HistoryEventType Type;

    // For field + assoc events
    public readonly Guid FldId;

    // For assoc events
    public readonly Guid ObjBId;
    public readonly Guid FldBId;

    // For field events (decoded)
    public readonly byte[] OldValue;
    public readonly byte[] NewValue;

    // For ObjCreated
    public readonly Guid TypId;

    private HistoryEvent(
        HistoryEventType type,
        Guid fldId,
        Guid objBId,
        Guid fldBId,
        byte[] oldValue,
        byte[] newValue,
        Guid typId)
    {
        Type = type;
        FldId = fldId;
        ObjBId = objBId;
        FldBId = fldBId;
        OldValue = oldValue;
        NewValue = newValue;
        TypId = typId;
    }

    public static HistoryEvent ObjCreated(Guid typId) => new(HistoryEventType.ObjCreated, Guid.Empty, Guid.Empty, Guid.Empty, [], [], typId);
    public static HistoryEvent ObjDeleted() => new(HistoryEventType.ObjDeleted, Guid.Empty, Guid.Empty, Guid.Empty, [], [], Guid.Empty);

    public static HistoryEvent FldChanged(Guid fldId, byte[] oldValue, byte[] newValue) => new(HistoryEventType.FldChanged, fldId, Guid.Empty, Guid.Empty, oldValue, newValue, Guid.Empty);

    public static HistoryEvent AsoAdded(Guid fldAId, Guid objBId, Guid fldBId) => new(HistoryEventType.AsoAdded, fldAId, objBId, fldBId, [], [], Guid.Empty);

    public static HistoryEvent AsoRemoved(Guid fldAId, Guid objBId, Guid fldBId) => new(HistoryEventType.AsoRemoved, fldAId, objBId, fldBId, [], [], Guid.Empty);
}

public sealed class HistoryCommit
{
    public required SortedId CommitId;
    public required DateTime TimestampUtc;
    public required Guid UserId;

    public required Dictionary<Guid, List<HistoryEvent>> EventsByObject;
}

//id according to GuidV8 custom format (https://www.rfc-editor.org/rfc/rfc9562#name-uuid-version-8)
//should be the same as guid v7, but with a better memory layout so that
//it works for sorting with lmdb without a custom compare function
[InlineArray(16)]
public struct SortedId
{
    public byte First;

    //todo performance test this....
    public static SortedId Create()
    {
        long unixTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var id = new SortedId();

        //the first 6bytes (48 bits) are the timestamp
        unixTs.AsSpan().Slice(0, 6).CopyToReverse(id.AsSpan());

        //the latter 10bytes (80 bits) are from a normal guid
        var guid = Guid.NewGuid();
        var guidSpan = guid.AsSpan();
        guidSpan.Slice(6).CopyTo(id.AsSpan().Slice(6));

        const byte versionMask = 0b11110000;
        const byte versionValue = 0b10000000;

        const byte variantMask = 0b11000000;
        const byte variantValue = 0b10000000;

        //set ver
        id[6] = (byte)((id[6] & ~versionMask) | versionValue);

        //set var
        id[8] = (byte)((id[8] & ~variantMask) | variantValue);

        return id;
    }

    public override string ToString()
    {
        Span<byte> bytes = this.AsSpan();
        Span<char> chars = stackalloc char[36];

        static char Hex(int v) => (char)(v < 10 ? '0' + v : 'a' + (v - 10));

        int ci = 0;

        for (int i = 0; i < 16; i++)
        {
            if (i == 4 || i == 6 || i == 8 || i == 10)
                chars[ci++] = '-';

            byte b = bytes[i];
            chars[ci++] = Hex(b >> 4);
            chars[ci++] = Hex(b & 0xF);
        }

        return new string(chars);
    }
}

/// <summary>
/// History storage:
/// - Commit record stored in <see cref="DbEnvironment.HistoryDb"/> with key = UUIDv7 bytes.
/// - Object index stored in <see cref="DbEnvironment.HistoryObjIndexDb"/> as dupsort: objId -> commitId.
/// </summary>
public static class History
{
    private const byte RecordVersion = 1;

    struct CommitHeader
    {
        public byte Version;
        public long UtcTimestamp;
        public Guid UserId;

        public RelativePtr<ObjBlockHeader> First;
    }

    struct ObjBlockHeader
    {
        public Guid ObjId;

        public RelativePtr<CommitEvent> FirstEvent;

        public RelativePtr<ObjBlockHeader> Next;
    }

    //This struct is way larger than it needs to be, we could do a bunch of things, like split it into multiple structs depending on the type.
    //It was originally designed to be this way, that's why we have a next ptr, so that the entries can have different sizes.
    //This struct get stored directly in the db, that's why it is important to keep it small
    struct CommitEvent
    {
        public HistoryEventType Type;

        // For field + assoc events
        public Guid FldId;

        // For assoc events
        public Guid ObjBId;
        public Guid FldBId;

        // For field events (decoded)
        public RelativePtr<byte> OldValue;
        public int OldValueLength;
        public RelativePtr<byte> NewValue;
        public int NewValueLength;

        // For ObjCreated
        public Guid TypId;

        public RelativePtr<CommitEvent> Next;
    }

    public static unsafe void WriteCommit(
        Arena transactionArena,
        DbEnvironment dbEnvironment,
        LightningTransaction writeTransaction,
        BPlusTree changeSet,
        DateTime timestampUtc,
        Guid userId)
    {
        using var scope = transactionArena.Scope();

        var header = transactionArena.Allocate(new CommitHeader
        {
            Version = RecordVersion,
            UtcTimestamp = timestampUtc.ToUniversalTime().Ticks,
            UserId = userId,
        });

        var commitKey = SortedId.Create();

        Guid currentObjId = Guid.Empty;
        bool hasCurrentObj = false;

        RelativePtr<ObjBlockHeader>* nextObjHeaderPtrLocation = &header->First;
        RelativePtr<CommitEvent>* nextCommitEventPtrLocation = null;

        bool currentObjDeleted = false;

        var changeCursor = changeSet.CreateCursor();
        Span<byte> startKey = stackalloc byte[2];
        startKey[0] = 0;
        startKey[1] = 0;

        if (changeCursor.SetRange(startKey) == ResultCode.Success)
        {
            //loop over the changes, they are already grouped by obj
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

                ObjBlockHeader* o;

                //write obj block header if needed
                if (!hasCurrentObj || objId != currentObjId)
                {
                    currentObjId = objId;
                    hasCurrentObj = true;
                    currentObjDeleted = false;

                    // Object block header
                    o = transactionArena.Allocate(new ObjBlockHeader
                    {
                        ObjId = objId
                    });
                    *nextObjHeaderPtrLocation = new RelativePtr<ObjBlockHeader>(header, o);
                    nextObjHeaderPtrLocation = &o->Next;

                    nextCommitEventPtrLocation = &o->FirstEvent;

                    // objId -> commitId index
                    writeTransaction.Put(dbEnvironment.HistoryObjIndexDb, objId.AsSpan(), commitKey.AsSpan());
                }

                // OBJ
                if (key.Length == 16)
                {
                    if (flag == ValueFlag.AddModify)
                    {
                        var e = transactionArena.Allocate(new CommitEvent
                        {
                            Type = HistoryEventType.ObjCreated,
                            TypId = MemoryMarshal.Read<Guid>(value),
                        });

                        *nextCommitEventPtrLocation = new RelativePtr<CommitEvent>(header, e);
                        nextCommitEventPtrLocation = &e->Next;
                    }
                    else if (flag == ValueFlag.Delete)
                    {
                        var e = transactionArena.Allocate(new CommitEvent
                        {
                            Type = HistoryEventType.ObjDeleted,
                        });

                        *nextCommitEventPtrLocation = new RelativePtr<CommitEvent>(header, e);
                        nextCommitEventPtrLocation = &e->Next;

                        currentObjDeleted = true;
                    }

                    continue;
                }

                // VAL
                if (key.Length == 32)
                {
                    // If the object is deleted in this commit, skip field-level noise.
                    if (currentObjDeleted)
                        continue;

                    // Record both Add/Modify and Delete (defaulting).
                    if (flag != ValueFlag.AddModify && flag != ValueFlag.Delete)
                        continue;

                    var fldId = MemoryMarshal.Read<Guid>(key.Slice(16));

                    var e = transactionArena.Allocate(new CommitEvent
                    {
                        Type = HistoryEventType.FldChanged,
                        FldId = fldId
                    });

                    *nextCommitEventPtrLocation = new RelativePtr<CommitEvent>(header, e);
                    nextCommitEventPtrLocation = &e->Next;

                    // Old payload
                    var (oldRes, _, oldVal) = writeTransaction.Get(dbEnvironment.ObjectDb, key);
                    ReadOnlySpan<byte> oldPayload = ReadOnlySpan<byte>.Empty;
                    if (oldRes == MDBResultCode.Success)
                    {
                        var s = oldVal.AsSpan();
                        if (s.Length > 1)
                            oldPayload = s.Slice(1);
                    }

                    e->OldValueLength = oldPayload.Length;
                    e->OldValue = new RelativePtr<byte>(header, transactionArena.AllocateSlice(oldPayload).Items);

                    // New payload
                    ReadOnlySpan<byte> newValue = ReadOnlySpan<byte>.Empty;
                    if (flag == ValueFlag.AddModify)
                    {
                        // changeset value is [ValueTyp][payload]
                        newValue = value.Length > 1 ? value.Slice(1) : ReadOnlySpan<byte>.Empty;
                    }

                    e->NewValueLength = newValue.Length;
                    e->NewValue = new RelativePtr<byte>(header, transactionArena.AllocateSlice(newValue).Items);

                    continue;
                }

                // ASO
                if (key.Length == 64)
                {
                    var e = transactionArena.Allocate(new CommitEvent
                    {
                        Type = flag == ValueFlag.AddModify ? HistoryEventType.AsoAdded : HistoryEventType.AsoRemoved,
                        FldId = MemoryMarshal.Read<Guid>(key.Slice(16)),
                        FldBId = MemoryMarshal.Read<Guid>(key.Slice(48)),
                        ObjBId = MemoryMarshal.Read<Guid>(key.Slice(32))
                    });

                    *nextCommitEventPtrLocation = new RelativePtr<CommitEvent>(header, e);
                    nextCommitEventPtrLocation = &e->Next;
                }
            } while (changeCursor.Next().ResultCode == ResultCode.Success);
        }

        var commitData = transactionArena.GetRegion((byte*)header);

        // Write commit record
        writeTransaction.Put(dbEnvironment.HistoryDb, commitKey.AsSpan(), commitData);
    }

    public static IEnumerable<SortedId> GetCommitsForObject(DbEnvironment dbEnvironment, LightningTransaction readTransaction, Guid objId)
    {
        using var cursor = readTransaction.CreateCursor(dbEnvironment.HistoryObjIndexDb);

        if (cursor.SetKey(objId.AsSpan()).resultCode != MDBResultCode.Success)
            yield break;

        do
        {
            var (_, _, value) = cursor.GetCurrent();
            yield return MemoryMarshal.Read<SortedId>(value.AsSpan());
        } while (cursor.NextDuplicate().resultCode == MDBResultCode.Success);
    }

    public static HistoryCommit? TryGetCommit(DbEnvironment dbEnvironment, LightningTransaction readTransaction, SortedId commitId)
    {
        var (rc, _, value) = readTransaction.Get(dbEnvironment.HistoryDb, commitId.AsSpan());
        if (rc != MDBResultCode.Success)
            return null;

        return DecodeCommitValue(commitId, value.AsSlice());
    }

    public static IEnumerable<HistoryCommit> GetAllCommits(DbEnvironment dbEnvironment, LightningTransaction readTransaction, int max = int.MaxValue)
    {
        using var cursor = readTransaction.CreateCursor(dbEnvironment.HistoryDb);
        if (cursor.First().resultCode != MDBResultCode.Success)
            yield break;

        int count = 0;
        do
        {
            var (_, key, value) = cursor.GetCurrent();
            var keySpan = key.AsSpan();

            if (keySpan.Length != 16)
                continue;

            var commitId = MemoryMarshal.Read<SortedId>(keySpan);
            yield return DecodeCommitValue(commitId, value.AsSlice());

            count++;
            if (count >= max)
                yield break;
        } while (cursor.Next().resultCode == MDBResultCode.Success);
    }

    private static unsafe ReadOnlySpan<T> ReadSpan<T>(Slice<byte> data, nint location, int length) where T : unmanaged
    {
        var d = data.Length - location;
        if (d < sizeof(T) * length)
            throw new Exception("Should never happen!!!!");

        return new ReadOnlySpan<T>((T*)(data.Items + location), length);
    }

    private static unsafe T* Read<T>(Slice<byte> data, nint location) where T : unmanaged
    {
        var d = data.Length - location;
        if (d < sizeof(T))
            throw new Exception("Should never happen!!!!");

        return (T*)(data.Items + location);
    }

    private static unsafe T* Read<T>(Slice<byte> data, RelativePtr<T> ptr) where T : unmanaged
    {
        return Read<T>(data, ptr.Offset);
    }

    private static unsafe HistoryCommit DecodeCommitValue(SortedId commitId, Slice<byte> data)
    {
        var header = Read<CommitHeader>(data, 0);

        if (header->Version != RecordVersion)
            throw new InvalidOperationException($"Unsupported history record version {header->Version}");

        Dictionary<Guid, List<HistoryEvent>> eventsByObj = new();

        var nextObj = header->First;
        while (nextObj.Offset != 0)
        {
            var obj = Read(data, nextObj);
            nextObj = obj->Next;

            List<HistoryEvent> events = new();

            var nextEvent = obj->FirstEvent;
            while (nextEvent.Offset != 0)
            {
                var e = Read(data, nextEvent);
                nextEvent = e->Next;

                switch (e->Type)
                {
                    case HistoryEventType.ObjCreated:
                        events.Add(HistoryEvent.ObjCreated(e->TypId));
                        break;
                    case HistoryEventType.ObjDeleted:
                        events.Add(HistoryEvent.ObjDeleted());
                        break;
                    case HistoryEventType.FldChanged:
                        events.Add(HistoryEvent.FldChanged(e->FldId,
                            ReadSpan<byte>(data, e->OldValue.Offset, e->OldValueLength).ToArray(),
                            ReadSpan<byte>(data, e->NewValue.Offset, e->NewValueLength).ToArray()));
                        break;
                    case HistoryEventType.AsoAdded:
                        events.Add(HistoryEvent.AsoAdded(e->FldId, e->ObjBId, e->FldBId));
                        break;
                    case HistoryEventType.AsoRemoved:
                        events.Add(HistoryEvent.AsoRemoved(e->FldId, e->ObjBId, e->FldBId));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            eventsByObj.Add(obj->ObjId, events);
        }

        return new HistoryCommit
        {
            CommitId = commitId,
            TimestampUtc = new DateTime(header->UtcTimestamp, DateTimeKind.Utc),
            UserId = header->UserId,
            EventsByObject = eventsByObj
        };
    }
}