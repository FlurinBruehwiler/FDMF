// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

namespace FDMF.Core.DatabaseLayer;

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
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Name));
        set => DbSession.SetFldValue(ObjId, Fields.Name, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public bool IsIndexed
    {
        get => MemoryMarshal.Read<bool>(DbSession.GetFldValue(ObjId, Fields.IsIndexed));
        set => DbSession.SetFldValue(ObjId, Fields.IsIndexed, value.AsSpan());
    }

    [MemoryPackIgnore]
    public FieldDataType DataType
    {
        get => MemoryMarshal.Read<FieldDataType>(DbSession.GetFldValue(ObjId, Fields.DataType));
        set => DbSession.SetFldValue(ObjId, Fields.DataType, value.AsSpan());
    }

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
    public EnumDefinition? Enum
    {
        get => GeneratedCodeHelper.GetNullableAssoc<EnumDefinition>(DbSession, ObjId, Fields.Enum);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.Enum, value?.ObjId ?? Guid.Empty, FDMF.Core.DatabaseLayer.EnumDefinition.Fields.FieldUsage);
    }

    [MemoryPackIgnore]
    public EntityDefinition OwningEntity
    {
        get => GeneratedCodeHelper.GetAssoc<EntityDefinition>(DbSession, ObjId, Fields.OwningEntity);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.OwningEntity, value.ObjId, FDMF.Core.DatabaseLayer.EntityDefinition.Fields.FieldDefinitions);
    }


    public static implicit operator RootEntity(FieldDefinition value) => new RootEntity { DbSession = value.DbSession, ObjId = value.ObjId };

    public static explicit operator FieldDefinition(RootEntity value)
    {
        var actual = value.DbSession.GetTypId(value.ObjId);
        if (!GeneratedCodeHelper.IsAssignableFrom(value.DbSession, TypId, actual))
            throw new System.InvalidCastException("Cannot cast 'RootEntity' to 'FieldDefinition'");
        return new FieldDefinition { DbSession = value.DbSession, ObjId = value.ObjId };
    }

    public static bool TryCastFrom(RootEntity value, out FieldDefinition result)
    {
        var actual = value.DbSession.GetTypId(value.ObjId);
        if (GeneratedCodeHelper.IsAssignableFrom(value.DbSession, TypId, actual))
        {
            result = new FieldDefinition { DbSession = value.DbSession, ObjId = value.ObjId };
            return true;
        }
        result = default;
        return false;
    }

    public static bool operator ==(FieldDefinition a, FieldDefinition b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(FieldDefinition a, FieldDefinition b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(FieldDefinition other) => this == other;
    public override bool Equals(object? obj) => obj is FieldDefinition other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///42a6f33d-a938-4ad8-9682-aabdc92a53d2
    public static Guid TypId { get; } = new Guid([61, 243, 166, 66, 56, 169, 216, 74, 150, 130, 170, 189, 201, 42, 83, 210]);

    public static class Fields
    {
        ///c2ef8935-fcaf-4b33-aa31-59014a4c6765
        public static readonly Guid Name = new Guid([53, 137, 239, 194, 175, 252, 51, 75, 170, 49, 89, 1, 74, 76, 103, 101]);
        ///2e699a43-98b9-42f7-a26c-20256305b7cb
        public static readonly Guid IsIndexed = new Guid([67, 154, 105, 46, 185, 152, 247, 66, 162, 108, 32, 37, 99, 5, 183, 203]);
        ///b4f55a8c-6f73-41bc-b051-cbaeb18b0390
        public static readonly Guid DataType = new Guid([140, 90, 245, 180, 115, 111, 188, 65, 176, 81, 203, 174, 177, 139, 3, 144]);
        ///8a5d90d2-1b1c-428e-a010-9ae7d603dd30
        public static readonly Guid Key = new Guid([210, 144, 93, 138, 28, 27, 142, 66, 160, 16, 154, 231, 214, 3, 221, 48]);
        ///f91613e2-a9f9-4e3b-96e3-38650319dc0c
        public static readonly Guid Id = new Guid([226, 19, 22, 249, 249, 169, 59, 78, 150, 227, 56, 101, 3, 25, 220, 12]);
        ///ec74c01e-86b2-4334-b8cb-d800aaa827ae
        public static readonly Guid Enum = new Guid([30, 192, 116, 236, 178, 134, 52, 67, 184, 203, 216, 0, 170, 168, 39, 174]);
        ///f097fe25-1d11-47b3-a02c-0072a781a528
        public static readonly Guid OwningEntity = new Guid([37, 254, 151, 240, 17, 29, 179, 71, 160, 44, 0, 114, 167, 129, 165, 40]);
    }
}
