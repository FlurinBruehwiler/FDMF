using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using LightningDB;

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
    //   [objIdGuidLE:16][eventCount:int32][events...]
    // event formats:
    //   ObjCreated: [type:1][typIdGuidLE:16]
    //   ObjDeleted: [type:1]
    //   AsoAdded/AsoRemoved: [type:1][fldAIdGuidLE:16][objBIdGuidLE:16][fldBIdGuidLE:16]
    //   FldChanged: [type:1][fldIdGuidLE:16][oldOrigLen:int32][oldStoredLen:int32][oldBytes][newOrigLen:int32][newStoredLen:int32][newBytes]
    //
    // Notes:
    // - HistoryDb commit keys are UUIDv7 bytes in big-endian (sortable).
    // - Payload GUIDs are written in little-endian to avoid conversions on little-endian machines.
    // - HistoryObjIndexDb values are commitId bytes (big-endian) so dupsort keeps them time-ordered.

    public static Guid CreateCommitId() => Guid.CreateVersion7();

    public static PooledLease WriteCommit(
        Environment environment,
        LightningTransaction writeTransaction,
        BPlusTree changeSet,
        Guid commitId,
        DateTime timestampUtc,
        Guid userId,
        int maxStringBytes = 256)
    {
        Span<byte> commitKey = stackalloc byte[16];
        WriteGuidBigEndian(commitId, commitKey);

        Span<byte> guidScratch = stackalloc byte[16];
        var writer = new PooledBufferWriter(initialCapacity: 16 * 1024, guidScratch);

        // Header
        writer.WriteByte(RecordVersion);
        writer.WriteInt64(timestampUtc.ToUniversalTime().Ticks);
        writer.WriteGuidLittleEndian(userId);
        int objectCountOffset = writer.ReserveInt32();

        int objectCount = 0;

        Guid currentObjId = Guid.Empty;
        bool hasCurrentObj = false;
        int currentObjEventCount = 0;
        int currentObjEventCountOffset = 0;
        bool currentObjDeleted = false;

        var changeCursor = changeSet.CreateCursor();
        Span<byte> startKey = stackalloc byte[2];
        startKey[0] = 0;
        startKey[1] = 0;

        Span<byte> objKey = stackalloc byte[16];

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

                if (!hasCurrentObj || objId != currentObjId)
                {
                    if (hasCurrentObj)
                    {
                        writer.PatchInt32(currentObjEventCountOffset, currentObjEventCount);
                        objectCount++;
                    }

                    currentObjId = objId;
                    hasCurrentObj = true;
                    currentObjEventCount = 0;
                    currentObjDeleted = false;

                    // Object block header
                    writer.WriteGuidLittleEndian(objId);
                    currentObjEventCountOffset = writer.ReserveInt32();

                    // objId -> commitId index
                    WriteGuidLittleEndian(objId, objKey);
                    writeTransaction.Put(environment.HistoryObjIndexDb, objKey, commitKey);
                }

                // OBJ
                if (key.Length == 16)
                {
                    if (flag == ValueFlag.AddModify)
                    {
                        if (value.Length >= 1 + 16)
                        {
                            writer.WriteByte((byte)HistoryEventType.ObjCreated);
                            writer.WriteGuidLittleEndian(MemoryMarshal.Read<Guid>(value.Slice(1)));
                            currentObjEventCount++;
                        }
                    }
                    else if (flag == ValueFlag.Delete)
                    {
                        writer.WriteByte((byte)HistoryEventType.ObjDeleted);
                        currentObjDeleted = true;
                        currentObjEventCount++;
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

                    writer.WriteByte((byte)HistoryEventType.FldChanged);
                    writer.WriteGuidLittleEndian(fldId);

                    var dataType = environment.Model.FieldsById.TryGetValue(fldId, out var fldDef) ? fldDef.DataType : (FieldDataType?)null;

                    // Old payload
                    var (oldRes, _, oldVal) = writeTransaction.Get(environment.ObjectDb, key);
                    ReadOnlySpan<byte> oldPayload = ReadOnlySpan<byte>.Empty;
                    if (oldRes == MDBResultCode.Success)
                    {
                        var s = oldVal.AsSpan();
                        if (s.Length > 1)
                            oldPayload = s.Slice(1);
                    }

                    WriteValueBlob(ref writer, oldPayload, dataType, maxStringBytes);

                    // New payload
                    if (flag == ValueFlag.AddModify)
                    {
                        // changeset value is [ValueTyp][payload]
                        ReadOnlySpan<byte> newPayload = value.Length > 1 ? value.Slice(1) : ReadOnlySpan<byte>.Empty;
                        WriteValueBlob(ref writer, newPayload, dataType, maxStringBytes);
                    }
                    else
                    {
                        // Delete => missing/default
                        WriteValueBlob(ref writer, ReadOnlySpan<byte>.Empty, dataType, maxStringBytes);
                    }

                    currentObjEventCount++;
                    continue;
                }

                // ASO
                if (key.Length == 64)
                {
                    var fldAId = MemoryMarshal.Read<Guid>(key.Slice(16));
                    var objBId = MemoryMarshal.Read<Guid>(key.Slice(32));
                    var fldBId = MemoryMarshal.Read<Guid>(key.Slice(48));

                    writer.WriteByte((byte)(flag == ValueFlag.AddModify ? HistoryEventType.AsoAdded : HistoryEventType.AsoRemoved));
                    writer.WriteGuidLittleEndian(fldAId);
                    writer.WriteGuidLittleEndian(objBId);
                    writer.WriteGuidLittleEndian(fldBId);
                    currentObjEventCount++;
                }

            } while (changeCursor.Next().ResultCode == ResultCode.Success);
        }

        if (hasCurrentObj)
        {
            writer.PatchInt32(currentObjEventCountOffset, currentObjEventCount);
            objectCount++;
        }

        writer.PatchInt32(objectCountOffset, objectCount);

        // Write commit record
        writeTransaction.Put(environment.HistoryDb, commitKey, writer.Written);

        // Keep the underlying buffer alive until the LMDB write transaction commits.
        return writer.DetachLease();
    }

    public static IEnumerable<Guid> GetCommitsForObject(Environment environment, LightningTransaction readTransaction, Guid objId)
    {
        using var cursor = readTransaction.CreateCursor(environment.HistoryObjIndexDb);

        Span<byte> objKey = stackalloc byte[16];
        WriteGuidLittleEndian(objId, objKey);

        if (cursor.SetKey(objKey).resultCode != MDBResultCode.Success)
            yield break;

        do
        {
            var (_, _, value) = cursor.GetCurrent();
            yield return ReadGuidBigEndian(value.AsSpan());
        } while (cursor.NextDuplicate().resultCode == MDBResultCode.Success);
    }

    public static HistoryCommit? TryGetCommit(Environment environment, LightningTransaction readTransaction, Guid commitId)
    {
        Span<byte> commitKey = stackalloc byte[16];
        WriteGuidBigEndian(commitId, commitKey);

        var (rc, _, value) = readTransaction.Get(environment.HistoryDb, commitKey);
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

    private static void WriteValueBlob(ref PooledBufferWriter writer, ReadOnlySpan<byte> payload, FieldDataType? dataType, int maxStringBytes)
    {
        int originalLen = payload.Length;
        int storedLen = originalLen;

        if (dataType == FieldDataType.String && storedLen > maxStringBytes)
        {
            storedLen = maxStringBytes;
            if ((storedLen & 1) == 1)
                storedLen--;
        }

        writer.WriteInt32(originalLen);
        writer.WriteInt32(storedLen);
        if (storedLen > 0)
        {
            writer.WriteBytes(payload.Slice(0, storedLen));
        }
    }

    private static void WriteGuidBigEndian(Guid guid, Span<byte> destination)
    {
        guid.TryWriteBytes(destination, bigEndian: true, out _);
    }

    private static void WriteGuidLittleEndian(Guid guid, Span<byte> destination)
    {
        guid.TryWriteBytes(destination, bigEndian: false, out _);
    }

    private static Guid ReadGuidBigEndian(ReadOnlySpan<byte> source) => new(source, bigEndian: true);

    private static Guid ReadGuidLittleEndian(ReadOnlySpan<byte> source) => new(source);

    private static HistoryCommit DecodeCommitValue(Guid commitId, ReadOnlySpan<byte> data)
    {
        int offset = 0;

        byte version = data[offset++];
        if (version != RecordVersion)
            throw new InvalidOperationException($"Unsupported history record version {version}");

        long ticks = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8));
        offset += 8;

        var userId = ReadGuidLittleEndian(data.Slice(offset, 16));
        offset += 16;

        int objectCount = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;

        Dictionary<Guid, List<HistoryEvent>> eventsByObj = new(objectCount);

        for (int i = 0; i < objectCount; i++)
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
