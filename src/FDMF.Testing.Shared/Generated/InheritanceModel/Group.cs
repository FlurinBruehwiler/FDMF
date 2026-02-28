// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
#pragma warning disable CS0612 // Type or member is obsolete

namespace FDMF.Testing.Shared.InheritanceModelModel;

[MemoryPackable]
public partial struct Group : ITransactionObject, IEquatable<Group>
{
    [Obsolete]
    [MemoryPackConstructor]
    public Group() { }
    public Group(DbSession dbSession)
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
    public AssocCollection<BaseItem> Items => new(DbSession, ObjId, Fields.Items, BaseItem.Fields.Group);

    public static bool operator ==(Group a, Group b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(Group a, Group b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(Group other) => this == other;
    public override bool Equals(object? obj) => obj is Group other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///6144822d-3e49-4965-bf9c-628ede806504
    public static Guid TypId { get; } = new Guid([45, 130, 68, 97, 73, 62, 101, 73, 191, 156, 98, 142, 222, 128, 101, 4]);

    public static class Fields
    {
        ///2b517398-e101-4e1f-9d5c-73d8f1cfe0a7
        public static readonly Guid Name = new Guid([152, 115, 81, 43, 1, 225, 31, 78, 157, 92, 115, 216, 241, 207, 224, 167]);
        ///7eee79bf-e568-4b0f-a3c5-e6952b7bd425
        public static readonly Guid Items = new Guid([191, 121, 238, 126, 104, 229, 15, 75, 163, 197, 230, 149, 43, 123, 212, 37]);
    }
}
