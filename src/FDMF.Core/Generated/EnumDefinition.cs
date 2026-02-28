// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

namespace FDMF.Core.DatabaseLayer;

[MemoryPackable]
public partial struct EnumDefinition : ITransactionObject, IEquatable<EnumDefinition>
{
    [Obsolete]
    [MemoryPackConstructor]
    public EnumDefinition() { }
    public EnumDefinition(DbSession dbSession)
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
    public string Variants
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Variants));
        set => DbSession.SetFldValue(ObjId, Fields.Variants, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public AssocCollection<FieldDefinition> FieldUsage => new(DbSession, ObjId, Fields.FieldUsage, FieldDefinition.Fields.Enum);


    public static implicit operator RootEntity(EnumDefinition value) => new RootEntity { DbSession = value.DbSession, ObjId = value.ObjId };

    public static explicit operator EnumDefinition(RootEntity value)
    {
        var actual = value.DbSession.GetTypId(value.ObjId);
        if (!GeneratedCodeHelper.IsAssignableFrom(value.DbSession, TypId, actual))
            throw new System.InvalidCastException("Cannot cast 'RootEntity' to 'EnumDefinition'");
        return new EnumDefinition { DbSession = value.DbSession, ObjId = value.ObjId };
    }

    public static bool TryCastFrom(RootEntity value, out EnumDefinition result)
    {
        var actual = value.DbSession.GetTypId(value.ObjId);
        if (GeneratedCodeHelper.IsAssignableFrom(value.DbSession, TypId, actual))
        {
            result = new EnumDefinition { DbSession = value.DbSession, ObjId = value.ObjId };
            return true;
        }
        result = default;
        return false;
    }

    public static bool operator ==(EnumDefinition a, EnumDefinition b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(EnumDefinition a, EnumDefinition b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(EnumDefinition other) => this == other;
    public override bool Equals(object? obj) => obj is EnumDefinition other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///9daec113-7ff2-4c52-8ba1-3d400ac39b7b
    public static Guid TypId { get; } = new Guid([19, 193, 174, 157, 242, 127, 82, 76, 139, 161, 61, 64, 10, 195, 155, 123]);

    public static class Fields
    {
        ///30a6d113-3c2a-45a6-b035-4d4ad6987dd7
        public static readonly Guid Name = new Guid([19, 209, 166, 48, 42, 60, 166, 69, 176, 53, 77, 74, 214, 152, 125, 215]);
        ///73b66d67-22c3-4a72-8309-d5ab81d483bc
        public static readonly Guid Variants = new Guid([103, 109, 182, 115, 195, 34, 114, 74, 131, 9, 213, 171, 129, 212, 131, 188]);
        ///ec0d2deb-efe4-4620-9e77-92df907a5024
        public static readonly Guid FieldUsage = new Guid([235, 45, 13, 236, 228, 239, 32, 70, 158, 119, 146, 223, 144, 122, 80, 36]);
    }
}
