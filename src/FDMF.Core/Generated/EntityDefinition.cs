// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
#pragma warning disable CS0612 // Type or member is obsolete

namespace FDMF.Core.DatabaseLayer;

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
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Key));
        set => DbSession.SetFldValue(ObjId, Fields.Key, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public Guid Id
    {
        get => MemoryMarshal.Read<Guid>(DbSession.GetFldValue(ObjId, Fields.Id));
        set => DbSession.SetFldValue(ObjId, Fields.Id, value.AsSpan());
    }

    [MemoryPackIgnore]
    public string Name
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Name));
        set => DbSession.SetFldValue(ObjId, Fields.Name, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public Model Model
    {
        get => GeneratedCodeHelper.GetAssoc<Model>(DbSession, ObjId, Fields.Model);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.Model, value.ObjId, FDMF.Core.DatabaseLayer.Model.Fields.EntityDefinitions);
    }

    [MemoryPackIgnore]
    public EntityDefinition? Parent
    {
        get => GeneratedCodeHelper.GetNullableAssoc<EntityDefinition>(DbSession, ObjId, Fields.Parent);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.Parent, value?.ObjId ?? Guid.Empty, FDMF.Core.DatabaseLayer.EntityDefinition.Fields.Children);
    }

    [MemoryPackIgnore]
    public AssocCollection<EntityDefinition> Children => new(DbSession, ObjId, Fields.Children, EntityDefinition.Fields.Parent);

    [MemoryPackIgnore]
    public AssocCollection<ReferenceFieldDefinition> ReferenceFieldDefinitions => new(DbSession, ObjId, Fields.ReferenceFieldDefinitions, ReferenceFieldDefinition.Fields.OwningEntity);

    [MemoryPackIgnore]
    public AssocCollection<FieldDefinition> FieldDefinitions => new(DbSession, ObjId, Fields.FieldDefinitions, FieldDefinition.Fields.OwningEntity);


    public static implicit operator RootEntity(EntityDefinition value) => new RootEntity { DbSession = value.DbSession, ObjId = value.ObjId };

    public static explicit operator EntityDefinition(RootEntity value)
    {
        var actual = value.DbSession.GetTypId(value.ObjId);
        if (!GeneratedCodeHelper.IsAssignableFrom(value.DbSession, TypId, actual))
            throw new System.InvalidCastException("Cannot cast 'RootEntity' to 'EntityDefinition'");
        return new EntityDefinition { DbSession = value.DbSession, ObjId = value.ObjId };
    }

    public static bool TryCastFrom(RootEntity value, out EntityDefinition result)
    {
        var actual = value.DbSession.GetTypId(value.ObjId);
        if (GeneratedCodeHelper.IsAssignableFrom(value.DbSession, TypId, actual))
        {
            result = new EntityDefinition { DbSession = value.DbSession, ObjId = value.ObjId };
            return true;
        }
        result = default;
        return false;
    }

    public static bool operator ==(EntityDefinition a, EntityDefinition b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(EntityDefinition a, EntityDefinition b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(EntityDefinition other) => this == other;
    public override bool Equals(object? obj) => obj is EntityDefinition other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///c15f876f-4f74-4034-9acb-03bc3b521e81
    public static Guid TypId { get; } = new Guid([111, 135, 95, 193, 116, 79, 52, 64, 154, 203, 3, 188, 59, 82, 30, 129]);

    public static class Fields
    {
        ///95f47b2f-a39e-4031-b12f-f30e0762d671
        public static readonly Guid Key = new Guid([47, 123, 244, 149, 158, 163, 49, 64, 177, 47, 243, 14, 7, 98, 214, 113]);
        ///16800d40-9d5c-41c6-8642-bb956def6e17
        public static readonly Guid Id = new Guid([64, 13, 128, 22, 92, 157, 198, 65, 134, 66, 187, 149, 109, 239, 110, 23]);
        ///4546ebea-3552-4198-8730-89182dc91a34
        public static readonly Guid Name = new Guid([234, 235, 70, 69, 82, 53, 152, 65, 135, 48, 137, 24, 45, 201, 26, 52]);
        ///d2b98e24-5786-4502-b2cf-5dea14097765
        public static readonly Guid Model = new Guid([36, 142, 185, 210, 134, 87, 2, 69, 178, 207, 93, 234, 20, 9, 119, 101]);
        ///15928c28-398d-4caa-97d1-7c01e9020d9f
        public static readonly Guid Parent = new Guid([40, 140, 146, 21, 141, 57, 170, 76, 151, 209, 124, 1, 233, 2, 13, 159]);
        ///232c2342-682d-4526-a5fd-f943830d7bef
        public static readonly Guid Children = new Guid([66, 35, 44, 35, 45, 104, 38, 69, 165, 253, 249, 67, 131, 13, 123, 239]);
        ///06950fac-de4f-487e-aa78-7095909805e4
        public static readonly Guid ReferenceFieldDefinitions = new Guid([172, 15, 149, 6, 79, 222, 126, 72, 170, 120, 112, 149, 144, 152, 5, 228]);
        ///8dc642e4-af58-403a-bed2-ce41baa95b21
        public static readonly Guid FieldDefinitions = new Guid([228, 66, 198, 141, 88, 175, 58, 64, 190, 210, 206, 65, 186, 169, 91, 33]);
    }
}
