// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

namespace FDMF.Core.DatabaseLayer;

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
    public string Name
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Name));
        set => DbSession.SetFldValue(ObjId, Fields.Name, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public Guid Id
    {
        get => MemoryMarshal.Read<Guid>(DbSession.GetFldValue(ObjId, Fields.Id));
        set => DbSession.SetFldValue(ObjId, Fields.Id, value.AsSpan());
    }

    [MemoryPackIgnore]
    public RefType RefType
    {
        get => MemoryMarshal.Read<RefType>(DbSession.GetFldValue(ObjId, Fields.RefType));
        set => DbSession.SetFldValue(ObjId, Fields.RefType, value.AsSpan());
    }

    [MemoryPackIgnore]
    public string Key
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Key));
        set => DbSession.SetFldValue(ObjId, Fields.Key, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public EntityDefinition OwningEntity
    {
        get => GeneratedCodeHelper.GetAssoc<EntityDefinition>(DbSession, ObjId, Fields.OwningEntity);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.OwningEntity, value.ObjId, FDMF.Core.DatabaseLayer.EntityDefinition.Fields.ReferenceFieldDefinitions);
    }

    [MemoryPackIgnore]
    public ReferenceFieldDefinition OtherReferenceFields
    {
        get => GeneratedCodeHelper.GetAssoc<ReferenceFieldDefinition>(DbSession, ObjId, Fields.OtherReferenceFields);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.OtherReferenceFields, value.ObjId, FDMF.Core.DatabaseLayer.ReferenceFieldDefinition.Fields.OtherReferenceFields);
    }

    public static bool operator ==(ReferenceFieldDefinition a, ReferenceFieldDefinition b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(ReferenceFieldDefinition a, ReferenceFieldDefinition b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(ReferenceFieldDefinition other) => this == other;
    public override bool Equals(object? obj) => obj is ReferenceFieldDefinition other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///2147ed0f-b37d-4429-a3f8-8312c1620383
    public static Guid TypId { get; } = new Guid([15, 237, 71, 33, 125, 179, 41, 68, 163, 248, 131, 18, 193, 98, 3, 131]);

    public static class Fields
    {
        ///32734a0c-eba5-4b97-80de-081857b356cc
        public static readonly Guid Name = new Guid([12, 74, 115, 50, 165, 235, 151, 75, 128, 222, 8, 24, 87, 179, 86, 204]);
        ///eff45530-7e4e-401f-a53e-094f0d815e8a
        public static readonly Guid Id = new Guid([48, 85, 244, 239, 78, 126, 31, 64, 165, 62, 9, 79, 13, 129, 94, 138]);
        ///924ee396-a747-4790-a9da-051e3e8676a8
        public static readonly Guid RefType = new Guid([150, 227, 78, 146, 71, 167, 144, 71, 169, 218, 5, 30, 62, 134, 118, 168]);
        ///450c77f2-73ff-4125-a879-b75840275c99
        public static readonly Guid Key = new Guid([242, 119, 12, 69, 255, 115, 37, 65, 168, 121, 183, 88, 64, 39, 92, 153]);
        ///500e3a9c-c469-4585-a7c4-e307dc295d88
        public static readonly Guid OwningEntity = new Guid([156, 58, 14, 80, 105, 196, 133, 69, 167, 196, 227, 7, 220, 41, 93, 136]);
        ///50ac96bd-7082-4821-95fa-57e9040246ab
        public static readonly Guid OtherReferenceFields = new Guid([189, 150, 172, 80, 130, 112, 33, 72, 149, 250, 87, 233, 4, 2, 70, 171]);
    }
}
