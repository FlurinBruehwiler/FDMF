using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using LightningDB;
using Shared.Utils;

namespace Shared.Database;

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
    public required Guid CommitId;
    public required DateTime TimestampUtc;
    public required Guid UserId;

    public required Dictionary<Guid, List<HistoryEvent>> EventsByObject;
}

/// <summary>
/// History storage:
/// - Commit record stored in <see cref="Shared.Environment.HistoryDb"/> with key = UUIDv7 (big-endian bytes), enabling linear scan of commits.
/// - Object index stored in <see cref="Shared.Environment.HistoryObjIndexDb"/> as dupsort: objId -> commitId (also time-ordered by UUIDv7 bytes).
///
/// Performance:
/// - Write path avoids managed allocations by using ArrayPool-backed buffers and streaming encoding.
/// </summary>
public static class History
{
    public readonly struct PooledLease : IDisposable
    {
        private readonly byte[] _buffer;
        private readonly int _length;

        public PooledLease(byte[] buffer, int length)
        {
            _buffer = buffer;
            _length = length;
        }

        public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, _length);

        public void Dispose()
        {
            if (_buffer.Length != 0)
                ArrayPool<byte>.Shared.Return(_buffer);
        }
    }

    private const byte RecordVersion = 1;

    // Commit record layout (binary, no per-field serialization overhead):
    // [version:1][timestampUtcTicks:int64][userIdGuidLE:16][objectCount:int32]
    // repeated object blocks:
    //   [objIdGuid:16][eventCount:int32][events...]
    // event formats:
    //   ObjCreated/Deleted: [type:1][typIdGuidLE:16]
    //   AsoAdded/AsoRemoved: [type:1][fldAIdGuid:16][objBIdGuid:16][fldBIdGuid:16]
    //   FldChanged: [type:1][fldIdGuid:16][oldOrigLen:int32][oldStoredLen:int32][oldBytes][newOrigLen:int32][newStoredLen:int32][newBytes]
    //
    // Notes:
    // - HistoryDb commit keys are UUIDv7 bytes in big-endian (sortable).
    // - Payload GUIDs are written in little-endian to avoid conversions on little-endian machines.
    // - HistoryObjIndexDb values are commitId bytes (big-endian) so dupsort keeps them time-ordered.


    unsafe struct CommitHeader
    {
        public byte Version;
        public long UtcTimestamp;
        public Guid UserId;

        public ObjBlockHeader* First;
    }

    unsafe struct ObjBlockHeader
    {
        public Guid ObjId;

        public CommitEvent* Event;

        public ObjBlockHeader* Next;
    }

    unsafe struct CommitEvent
    {
        public HistoryEventType Type;
        public void* Data;
        public CommitEvent* Next;
    }

    unsafe struct FldCommitEvent
    {
        public Guid FldId;

        public byte* OldData;
        public int OldDataLen;

        public byte* NewData;
        public int NewDataLen;
    }

    struct ObjCommitEvent
    {
        public Guid TypId;
    }

    struct AsoCommitEvent
    {
        public Guid FldIdA;
        public Guid FldIdB;
        public Guid ObjIdB;
    }

    public static unsafe void WriteCommit(
        Arena transactionArena,
        Environment environment,
        LightningTransaction writeTransaction,
        BPlusTree changeSet,
        DateTime timestampUtc,
        Guid userId)
    {
        var header = transactionArena.Allocate(new CommitHeader
        {
            Version = RecordVersion,
            UtcTimestamp = timestampUtc.ToUniversalTime().Ticks,
            UserId = userId,
        });

        var commitKey = Guid.CreateVersion7();

        Guid currentObjId = Guid.Empty;
        bool hasCurrentObj = false;

        ObjBlockHeader* currentObjBlockHeader = null;

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

                //write obj block header if needed
                if (!hasCurrentObj || objId != currentObjId)
                {
                    header->ObjCount++;

                    currentObjId = objId;
                    hasCurrentObj = true;
                    currentObjDeleted = false;

                    // Object block header
                    currentObjBlockHeader = transactionArena.Allocate(new ObjBlockHeader
                    {
                        ObjId = objId
                    });

                    // objId -> commitId index
                    writeTransaction.Put(environment.HistoryObjIndexDb, objId.AsSpan(), commitKey.AsSpan());
                }

                // OBJ
                if (key.Length == 16)
                {
                    if (flag == ValueFlag.AddModify)
                    {
                        transactionArena.Allocate(new CommitEvent
                        {
                            Type = HistoryEventType.ObjCreated,
                            Data = transactionArena.Allocate(new ObjCommitEvent
                            {
                                TypId = MemoryMarshal.Read<Guid>(value)
                            })
                        });
                    }
                    else if (flag == ValueFlag.Delete)
                    {
                        transactionArena.Allocate(new CommitEvent
                        {
                            Type = HistoryEventType.ObjDeleted,
                        });

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

                    var fldCommitEvent = transactionArena.Allocate(new FldCommitEvent
                    {
                        Type = HistoryEventType.FldChanged,
                        FldId = fldId
                    });

                    // Old payload
                    var (oldRes, _, oldVal) = writeTransaction.Get(environment.ObjectDb, key);
                    ReadOnlySpan<byte> oldPayload = ReadOnlySpan<byte>.Empty;
                    if (oldRes == MDBResultCode.Success)
                    {
                        var s = oldVal.AsSpan();
                        if (s.Length > 1)
                            oldPayload = s.Slice(1);
                    }

                    fldCommitEvent->OldLen = oldPayload.Length;
                    transactionArena.AllocateSlice(oldPayload);

                    // New payload
                    ReadOnlySpan<byte> newValue = ReadOnlySpan<byte>.Empty;
                    if (flag == ValueFlag.AddModify)
                    {
                        // changeset value is [ValueTyp][payload]
                        newValue = value.Length > 1 ? value.Slice(1) : ReadOnlySpan<byte>.Empty;
                    }

                    fldCommitEvent->NewLen = newValue.Length;
                    transactionArena.AllocateSlice(newValue);

                    currentObjBlockHeader->EventCount++;
                    continue;
                }

                // ASO
                if (key.Length == 64)
                {
                    transactionArena.Allocate(new AsoCommitEvent
                    {
                        Type = flag == ValueFlag.AddModify ? HistoryEventType.AsoAdded : HistoryEventType.AsoRemoved,
                        FldIdA = MemoryMarshal.Read<Guid>(key.Slice(16)),
                        FldIdB = MemoryMarshal.Read<Guid>(key.Slice(48)),
                        ObjIdB = MemoryMarshal.Read<Guid>(key.Slice(32))
                    });

                    currentObjBlockHeader->EventCount++;
                }

            } while (changeCursor.Next().ResultCode == ResultCode.Success);
        }

        // Write commit record
        writeTransaction.Put(environment.HistoryDb, commitKey.AsSpan(), transactionArena.GetRegion((byte*)header));
    }

    public static IEnumerable<Guid> GetCommitsForObject(Environment environment, LightningTransaction readTransaction, Guid objId)
    {
        using var cursor = readTransaction.CreateCursor(environment.HistoryObjIndexDb);

        if (cursor.SetKey(objId.AsSpan()).resultCode != MDBResultCode.Success)
            yield break;

        do
        {
            var (_, _, value) = cursor.GetCurrent();
            yield return ReadGuidBigEndian(value.AsSpan());
        } while (cursor.NextDuplicate().resultCode == MDBResultCode.Success);
    }

    public static HistoryCommit? TryGetCommit(Environment environment, LightningTransaction readTransaction, Guid commitId)
    {
        var (rc, _, value) = readTransaction.Get(environment.HistoryDb, commitId.AsSpan());
        if (rc != MDBResultCode.Success)
            return null;

        return DecodeCommitValue(commitId, value.AsSpan());
    }

    public static IEnumerable<HistoryCommit> GetAllCommits(Environment environment, LightningTransaction readTransaction, int max = int.MaxValue)
    {
        using var cursor = readTransaction.CreateCursor(environment.HistoryDb);
        if (cursor.First().resultCode != MDBResultCode.Success)
            yield break;

        int count = 0;
        do
        {
            var (_, key, value) = cursor.GetCurrent();
            var keySpan = key.AsSpan();

            if (keySpan.Length != 16)
                continue;

            var commitId = ReadGuidBigEndian(keySpan);
            yield return DecodeCommitValue(commitId, value.AsSpan());

            count++;
            if (count >= max)
                yield break;

        } while (cursor.Next().resultCode == MDBResultCode.Success);
    }

    private static HistoryCommit DecodeCommitValue(Guid commitId, ReadOnlySpan<byte> data)
    {
        int offset = 0;

        byte version = data[offset++];
        if (version != RecordVersion)
            throw new InvalidOperationException($"Unsupported history record version {version}");

        var header = MemoryMarshal.Read<CommitHeader>(data);


        Dictionary<Guid, List<HistoryEvent>> eventsByObj = new(header.ObjCount);

        for (int i = 0; i < header.ObjCount; i++)
        {


            var objId = ReadGuidLittleEndian(data.Slice(offset, 16));
            offset += 16;

            int eventCount = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
            offset += 4;

            List<HistoryEvent> events = new(eventCount);

            for (int j = 0; j < eventCount; j++)
            {
                var type = (HistoryEventType)data[offset++];

                switch (type)
                {
                    case HistoryEventType.ObjCreated:
                    {
                        var typId = ReadGuidLittleEndian(data.Slice(offset, 16));
                        offset += 16;
                        events.Add(HistoryEvent.ObjCreated(typId));
                        break;
                    }
                    case HistoryEventType.ObjDeleted:
                        events.Add(HistoryEvent.ObjDeleted());
                        break;
                    case HistoryEventType.AsoAdded:
                    {
                        var fldA = ReadGuidLittleEndian(data.Slice(offset, 16));
                        offset += 16;
                        var objB = ReadGuidLittleEndian(data.Slice(offset, 16));
                        offset += 16;
                        var fldB = ReadGuidLittleEndian(data.Slice(offset, 16));
                        offset += 16;
                        events.Add(HistoryEvent.AsoAdded(fldA, objB, fldB));
                        break;
                    }
                    case HistoryEventType.AsoRemoved:
                    {
                        var fldA = ReadGuidLittleEndian(data.Slice(offset, 16));
                        offset += 16;
                        var objB = ReadGuidLittleEndian(data.Slice(offset, 16));
                        offset += 16;
                        var fldB = ReadGuidLittleEndian(data.Slice(offset, 16));
                        offset += 16;
                        events.Add(HistoryEvent.AsoRemoved(fldA, objB, fldB));
                        break;
                    }
                    case HistoryEventType.FldChanged:
                    {
                        var fldId = ReadGuidLittleEndian(data.Slice(offset, 16));
                        offset += 16;

                        var oldValue = ReadBlob(data, ref offset);
                        var newValue = ReadBlob(data, ref offset);
                        events.Add(HistoryEvent.FldChanged(fldId, oldValue, newValue));
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            eventsByObj.Add(objId, events);
        }

        return new HistoryCommit
        {
            CommitId = commitId,
            TimestampUtc = new DateTime(ticks, DateTimeKind.Utc),
            UserId = userId,
            EventsByObject = eventsByObj,
        };
    }

    private static byte[] ReadBlob(ReadOnlySpan<byte> data, ref int offset)
    {
        int originalLen = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;
        int storedLen = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;

        if (storedLen == 0)
            return [];

        var bytes = data.Slice(offset, storedLen).ToArray();
        offset += storedLen;

        _ = originalLen; // kept for future display
        return bytes;
    }
}
