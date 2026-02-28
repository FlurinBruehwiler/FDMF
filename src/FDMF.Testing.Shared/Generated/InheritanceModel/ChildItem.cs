// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
#pragma warning disable CS0612 // Type or member is obsolete

namespace FDMF.Testing.Shared.InheritanceModelModel;

[MemoryPackable]
public partial struct ChildItem : ITransactionObject, IEquatable<ChildItem>
{
    [Obsolete]
    [MemoryPackConstructor]
    public ChildItem() { }
    public ChildItem(DbSession dbSession)
    {
        DbSession = dbSession;
        ObjId = DbSession.CreateObj(TypId);
    }

    [MemoryPackIgnore]
    public DbSession DbSession { get; set; } = null!;
    public Guid ObjId { get; set; }

    [MemoryPackIgnore]
    public string BaseName
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.BaseName));
        set => DbSession.SetFldValue(ObjId, Fields.BaseName, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public long ChildValue
    {
        get => MemoryMarshal.Read<long>(DbSession.GetFldValue(ObjId, Fields.ChildValue));
        set => DbSession.SetFldValue(ObjId, Fields.ChildValue, value.AsSpan());
    }

    [MemoryPackIgnore]
    public Group? Group
    {
        get => GeneratedCodeHelper.GetNullableAssoc<Group>(DbSession, ObjId, Fields.Group);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.Group, value?.ObjId ?? Guid.Empty, FDMF.Testing.Shared.InheritanceModelModel.Group.Fields.Items);
    }


    public static implicit operator BaseItem(ChildItem value) => new BaseItem { DbSession = value.DbSession, ObjId = value.ObjId };

    public static explicit operator ChildItem(BaseItem value)
    {
        var actual = value.DbSession.GetTypId(value.ObjId);
        if (!GeneratedCodeHelper.IsAssignableFrom(value.DbSession, TypId, actual))
            throw new System.InvalidCastException("Cannot cast 'BaseItem' to 'ChildItem'");
        return new ChildItem { DbSession = value.DbSession, ObjId = value.ObjId };
    }

    public static bool TryCastFrom(BaseItem value, out ChildItem result)
    {
        var actual = value.DbSession.GetTypId(value.ObjId);
        if (GeneratedCodeHelper.IsAssignableFrom(value.DbSession, TypId, actual))
        {
            result = new ChildItem { DbSession = value.DbSession, ObjId = value.ObjId };
            return true;
        }
        result = default;
        return false;
    }

    public static bool operator ==(ChildItem a, ChildItem b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(ChildItem a, ChildItem b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(ChildItem other) => this == other;
    public override bool Equals(object? obj) => obj is ChildItem other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///aca85dd2-48ed-4a3a-8b26-90ba93941938
    public static Guid TypId { get; } = new Guid([210, 93, 168, 172, 237, 72, 58, 74, 139, 38, 144, 186, 147, 148, 25, 56]);

    public static class Fields
    {
        ///f7d083b3-c3d2-43ca-af3b-a5f0e0310139
        public static readonly Guid BaseName = new Guid([179, 131, 208, 247, 210, 195, 202, 67, 175, 59, 165, 240, 224, 49, 1, 57]);
        ///5bd92bb5-6b8a-4486-b818-35582c72a5aa
        public static readonly Guid ChildValue = new Guid([181, 43, 217, 91, 138, 107, 134, 68, 184, 24, 53, 88, 44, 114, 165, 170]);
        ///562f8a49-f54f-4331-9495-bcef384cc915
        public static readonly Guid Group = new Guid([73, 138, 47, 86, 79, 245, 49, 67, 148, 149, 188, 239, 56, 76, 201, 21]);
    }
}
