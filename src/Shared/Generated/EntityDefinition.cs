// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using Shared;
using Shared.Database;
namespace Model.Generated;

[MemoryPackable]
public partial struct EntityDefinition : ITransactionObject, IEquatable<EntityDefinition>
{
    [Obsolete]
    [MemoryPackConstructor]
    public EntityDefinition() { }
    public EntityDefinition(DbSession dbSession)
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
    public Model Model
    {
        get => GeneratedCodeHelper.GetAssoc<Model>(DbSession, ObjId, Fields.Model);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.Model, value.ObjId, Model.Fields.EntityDefinitions);
    }
    [MemoryPackIgnore]
    public AssocCollection<FieldDefinition> FieldDefinitions => new(DbSession, ObjId, Fields.FieldDefinitions, FieldDefinition.Fields.OwningEntity);
    [MemoryPackIgnore]
    public AssocCollection<FieldDefinition> ReferenceFieldDefinitions => new(DbSession, ObjId, Fields.ReferenceFieldDefinitions, FieldDefinition.Fields.OwningEntity);

    public static bool operator ==(EntityDefinition a, EntityDefinition b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(EntityDefinition a, EntityDefinition b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(EntityDefinition other) => this == other;
    public override bool Equals(object? obj) => obj is EntityDefinition other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    public static Guid TypId { get; } = new Guid([111, 135, 95, 193, 116, 79, 52, 64, 154, 203, 3, 188, 59, 82, 30, 129]);

    public static class Fields
    {
        public static readonly Guid Key = new Guid([47, 123, 244, 149, 158, 163, 49, 64, 177, 47, 243, 14, 7, 98, 214, 113]);
        public static readonly Guid Name = new Guid([234, 235, 70, 69, 82, 53, 152, 65, 135, 48, 137, 24, 45, 201, 26, 52]);
        public static readonly Guid Id = new Guid([64, 13, 128, 22, 92, 157, 198, 65, 134, 66, 187, 149, 109, 239, 110, 23]);
        public static readonly Guid Model = new Guid([36, 142, 185, 210, 134, 87, 2, 69, 178, 207, 93, 234, 20, 9, 119, 101]);
        public static readonly Guid FieldDefinitions = new Guid([228, 66, 198, 141, 88, 175, 58, 64, 190, 210, 206, 65, 186, 169, 91, 33]);
        public static readonly Guid ReferenceFieldDefinitions = new Guid([172, 15, 149, 6, 79, 222, 126, 72, 170, 120, 112, 149, 144, 152, 5, 228]);
    }
}
