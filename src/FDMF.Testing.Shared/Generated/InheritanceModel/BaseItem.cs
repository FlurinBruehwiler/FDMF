// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
#pragma warning disable CS0612 // Type or member is obsolete

namespace FDMF.Testing.Shared.InheritanceModelModel;

[MemoryPackable]
public partial struct BaseItem : ITransactionObject, IEquatable<BaseItem>
{
    [Obsolete]
    [MemoryPackConstructor]
    public BaseItem() { }
    public BaseItem(DbSession dbSession)
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
    public Group? Group
    {
        get => GeneratedCodeHelper.GetNullableAssoc<Group>(DbSession, ObjId, Fields.Group);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.Group, value?.ObjId ?? Guid.Empty, FDMF.Testing.Shared.InheritanceModelModel.Group.Fields.Items);
    }

    public static bool operator ==(BaseItem a, BaseItem b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(BaseItem a, BaseItem b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(BaseItem other) => this == other;
    public override bool Equals(object? obj) => obj is BaseItem other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///898a5155-92c0-4d7c-b1f2-1d3425b47e4d
    public static Guid TypId { get; } = new Guid([85, 81, 138, 137, 192, 146, 124, 77, 177, 242, 29, 52, 37, 180, 126, 77]);

    public static class Fields
    {
        ///f7d083b3-c3d2-43ca-af3b-a5f0e0310139
        public static readonly Guid BaseName = new Guid([179, 131, 208, 247, 210, 195, 202, 67, 175, 59, 165, 240, 224, 49, 1, 57]);
        ///562f8a49-f54f-4331-9495-bcef384cc915
        public static readonly Guid Group = new Guid([73, 138, 47, 86, 79, 245, 49, 67, 148, 149, 188, 239, 56, 76, 201, 21]);
    }
}
