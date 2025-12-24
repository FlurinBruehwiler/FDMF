// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using Shared;
using Shared.Database;
namespace Model.Generated;

[MemoryPackable]
public partial struct Folder : ITransactionObject, IEquatable<Folder>
{
    [Obsolete]
    [MemoryPackConstructor]
    public Folder() { }
    public Folder(DbSession dbSession)
    {
        DbSession = dbSession;
        ObjId = DbSession.CreateObj(TypId);
    }

    [MemoryPackIgnore]
    public DbSession DbSession { get; set; } = null!;
    public Guid ObjId { get; set; }

    [MemoryPackIgnore]
    public unsafe string Name
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Name).AsSpan());
        set => DbSession.SetFldValue(ObjId, Fields.Name, Encoding.Unicode.GetBytes(value).AsSpan().AsSlice());
    }
    [MemoryPackIgnore]
    public unsafe DateTime TestDateField
    {
        get => MemoryMarshal.Read<DateTime>(DbSession.GetFldValue(ObjId, Fields.TestDateField).AsSpan());
        set => DbSession.SetFldValue(ObjId, Fields.TestDateField, new Slice<DateTime>(&value, 1).AsByteSlice());
    }
    [MemoryPackIgnore]
    public unsafe decimal TestDecimalField
    {
        get => MemoryMarshal.Read<decimal>(DbSession.GetFldValue(ObjId, Fields.TestDecimalField).AsSpan());
        set => DbSession.SetFldValue(ObjId, Fields.TestDecimalField, new Slice<decimal>(&value, 1).AsByteSlice());
    }
    [MemoryPackIgnore]
    public AssocCollection<Folder> Subfolders => new(DbSession, ObjId, Fields.Subfolders, Folder.Fields.Parent);
    [MemoryPackIgnore]
    public Folder? Parent
    {
        get => GeneratedCodeHelper.GetNullableAssoc<Folder>(DbSession, ObjId, Fields.Parent);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.Parent, value?.ObjId ?? Guid.Empty, Folder.Fields.Subfolders);
    }

    public static bool operator ==(Folder a, Folder b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(Folder a, Folder b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(Folder other) => this == other;
    public override bool Equals(object? obj) => obj is Folder other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);

    public static Guid TypId { get; } = new Guid([139, 189, 204, 163, 86, 34, 75, 65, 164, 2, 26, 9, 28, 180, 7, 165]);

    public static class Fields
    {
        public static readonly Guid Name = new Guid([123, 108, 105, 24, 93, 11, 106, 64, 159, 76, 242, 232, 48, 204, 15, 85]);
        public static readonly Guid TestDateField = new Guid([77, 177, 175, 25, 230, 230, 250, 79, 148, 123, 180, 184, 97, 208, 17, 142]);
        public static readonly Guid TestDecimalField = new Guid([106, 146, 178, 37, 125, 231, 31, 74, 186, 117, 157, 60, 118, 172, 125, 215]);
        public static readonly Guid Subfolders = new Guid([118, 212, 11, 180, 163, 217, 84, 69, 162, 246, 79, 119, 96, 192, 255, 228]);
        public static readonly Guid Parent = new Guid([167, 173, 160, 131, 83, 186, 119, 72, 130, 98, 189, 71, 82, 14, 234, 199]);
    }
}
