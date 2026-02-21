// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

namespace FDMF.Core.DatabaseLayer;

[MemoryPackable]
public partial struct User : ITransactionObject, IEquatable<User>
{
    [Obsolete]
    [MemoryPackConstructor]
    public User() { }
    public User(DbSession dbSession)
    {
        DbSession = dbSession;
        ObjId = DbSession.CreateObj(TypId);
    }

    [MemoryPackIgnore]
    public DbSession DbSession { get; set; } = null!;
    public Guid ObjId { get; set; }

    public static bool operator ==(User a, User b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(User a, User b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(User other) => this == other;
    public override bool Equals(object? obj) => obj is User other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///095433D0-4E74-471D-9B11-AEA9538A86CC
    public static Guid TypId { get; } = new Guid([208, 51, 84, 9, 116, 78, 29, 71, 155, 17, 174, 169, 83, 138, 134, 204]);

    public static class Fields
    {
    }
}
