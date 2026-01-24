// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using FDMF.Core;
using FDMF.Core.Database;
using MemoryPack;

namespace Model.Generated;

[MemoryPackable]
public partial struct ReferenceFieldDefinition : ITransactionObject, IEquatable<ReferenceFieldDefinition>
{
    [Obsolete]
    [MemoryPackConstructor]
    public ReferenceFieldDefinition() { }
    public ReferenceFieldDefinition(DbSession dbSession)
    {
        DbSession = dbSession;
        ObjId = DbSession.CreateObj(TypId);
    }

    [MemoryPackIgnore]
    public DbSession DbSession { get; set; } = null!;
    public Guid ObjId { get; set; }

    [MemoryPackIgnore]
    public string Key
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Key).AsSpan());
        set => DbSession.SetFldValue(ObjId, Fields.Key, Encoding.Unicode.GetBytes(value));
    }
    [MemoryPackIgnore]
    public string Name
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Name).AsSpan());
        set => DbSession.SetFldValue(ObjId, Fields.Name, Encoding.Unicode.GetBytes(value));
    }
    [MemoryPackIgnore]
    public string Id
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Id).AsSpan());
        set => DbSession.SetFldValue(ObjId, Fields.Id, Encoding.Unicode.GetBytes(value));
    }
    [MemoryPackIgnore]
    public string RefType
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.RefType).AsSpan());
        set => DbSession.SetFldValue(ObjId, Fields.RefType, Encoding.Unicode.GetBytes(value));
    }
    [MemoryPackIgnore]
    public EntityDefinition OwningEntity
    {
        get => GeneratedCodeHelper.GetAssoc<EntityDefinition>(DbSession, ObjId, Fields.OwningEntity);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.OwningEntity, value.ObjId, EntityDefinition.Fields.ReferenceFieldDefinitions);
    }
    [MemoryPackIgnore]
    public ReferenceFieldDefinition OtherReferenceFields
    {
        get => GeneratedCodeHelper.GetAssoc<ReferenceFieldDefinition>(DbSession, ObjId, Fields.OtherReferenceFields);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.OtherReferenceFields, value.ObjId, ReferenceFieldDefinition.Fields.OtherReferenceFields);
    }

    public static bool operator ==(ReferenceFieldDefinition a, ReferenceFieldDefinition b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(ReferenceFieldDefinition a, ReferenceFieldDefinition b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(ReferenceFieldDefinition other) => this == other;
    public override bool Equals(object? obj) => obj is ReferenceFieldDefinition other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    public static Guid TypId { get; } = new Guid([15, 237, 71, 33, 125, 179, 41, 68, 163, 248, 131, 18, 193, 98, 3, 131]);

    public static class Fields
    {
        public static readonly Guid Key = new Guid([242, 119, 12, 69, 255, 115, 37, 65, 168, 121, 183, 88, 64, 39, 92, 153]);
        public static readonly Guid Name = new Guid([12, 74, 115, 50, 165, 235, 151, 75, 128, 222, 8, 24, 87, 179, 86, 204]);
        public static readonly Guid Id = new Guid([48, 85, 244, 239, 78, 126, 31, 64, 165, 62, 9, 79, 13, 129, 94, 138]);
        public static readonly Guid RefType = new Guid([150, 227, 78, 146, 71, 167, 144, 71, 169, 218, 5, 30, 62, 134, 118, 168]);
        public static readonly Guid OwningEntity = new Guid([156, 58, 14, 80, 105, 196, 133, 69, 167, 196, 227, 7, 220, 41, 93, 136]);
        public static readonly Guid OtherReferenceFields = new Guid([189, 150, 172, 80, 130, 112, 33, 72, 149, 250, 87, 233, 4, 2, 70, 171]);
    }
}
