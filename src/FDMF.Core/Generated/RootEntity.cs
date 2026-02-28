// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

namespace FDMF.Core.DatabaseLayer;

[MemoryPackable]
public partial struct RootEntity : ITransactionObject, IEquatable<RootEntity>
{
    [Obsolete]
    [MemoryPackConstructor]
    public RootEntity() { }
    public RootEntity(DbSession dbSession)
    {
        DbSession = dbSession;
        ObjId = DbSession.CreateObj(TypId);
    }

    [MemoryPackIgnore]
    public DbSession DbSession { get; set; } = null!;
    public Guid ObjId { get; set; }

    public static bool operator ==(RootEntity a, RootEntity b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(RootEntity a, RootEntity b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(RootEntity other) => this == other;
    public override bool Equals(object? obj) => obj is RootEntity other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///0c6d2581-7b18-4c35-bd16-3ae403bbf7a4
    public static Guid TypId { get; } = new Guid([129, 37, 109, 12, 24, 123, 53, 76, 189, 22, 58, 228, 3, 187, 247, 164]);

    public static class Fields
    {
    }
}
