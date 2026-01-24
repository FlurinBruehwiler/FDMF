// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using FDMF.Core;
using FDMF.Core.Database;
using MemoryPack;

namespace BaseModel.Generated;

[MemoryPackable]
public partial struct FieldDefinition : ITransactionObject, IEquatable<FieldDefinition>
{
    [Obsolete]
    [MemoryPackConstructor]
    public FieldDefinition() { }
    public FieldDefinition(DbSession dbSession)
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
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Name).AsSpan());
        set => DbSession.SetFldValue(ObjId, Fields.Name, Encoding.Unicode.GetBytes(value));
    }
    [MemoryPackIgnore]
    public bool IsIndexed
    {
        get => MemoryMarshal.Read<bool>(DbSession.GetFldValue(ObjId, Fields.IsIndexed).AsSpan());
        set => DbSession.SetFldValue(ObjId, Fields.IsIndexed, value.AsSpan());
    }
    [MemoryPackIgnore]
    public string DataType
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.DataType).AsSpan());
        set => DbSession.SetFldValue(ObjId, Fields.DataType, Encoding.Unicode.GetBytes(value));
    }
    [MemoryPackIgnore]
    public string Key
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Key).AsSpan());
        set => DbSession.SetFldValue(ObjId, Fields.Key, Encoding.Unicode.GetBytes(value));
    }
    [MemoryPackIgnore]
    public string Id
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Id).AsSpan());
        set => DbSession.SetFldValue(ObjId, Fields.Id, Encoding.Unicode.GetBytes(value));
    }
    [MemoryPackIgnore]
    public EntityDefinition OwningEntity
    {
        get => GeneratedCodeHelper.GetAssoc<EntityDefinition>(DbSession, ObjId, Fields.OwningEntity);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.OwningEntity, value.ObjId, EntityDefinition.Fields.FieldDefinitions);
    }

    public static bool operator ==(FieldDefinition a, FieldDefinition b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(FieldDefinition a, FieldDefinition b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(FieldDefinition other) => this == other;
    public override bool Equals(object? obj) => obj is FieldDefinition other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    public static Guid TypId { get; } = new Guid([61, 243, 166, 66, 56, 169, 216, 74, 150, 130, 170, 189, 201, 42, 83, 210]);

    public static class Fields
    {
        public static readonly Guid Name = new Guid([53, 137, 239, 194, 175, 252, 51, 75, 170, 49, 89, 1, 74, 76, 103, 101]);
        public static readonly Guid IsIndexed = new Guid([67, 154, 105, 46, 185, 152, 247, 66, 162, 108, 32, 37, 99, 5, 183, 203]);
        public static readonly Guid DataType = new Guid([140, 90, 245, 180, 115, 111, 188, 65, 176, 81, 203, 174, 177, 139, 3, 144]);
        public static readonly Guid Key = new Guid([210, 144, 93, 138, 28, 27, 142, 66, 160, 16, 154, 231, 214, 3, 221, 48]);
        public static readonly Guid Id = new Guid([226, 19, 22, 249, 249, 169, 59, 78, 150, 227, 56, 101, 3, 25, 220, 12]);
        public static readonly Guid OwningEntity = new Guid([37, 254, 151, 240, 17, 29, 179, 71, 160, 44, 0, 114, 167, 129, 165, 40]);
    }
}
