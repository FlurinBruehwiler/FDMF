// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

namespace FDMF.Testing.Shared.BusinessModelModel;

[MemoryPackable]
public partial struct Folder : ITransactionObject, IEquatable<Folder>
{
    [Obsolete]
    [MemoryPackConstructor]
    public Folder() { }
    public Folder(DbSession dbSession)
    {
        DbSession = dbSession;
        ObjId = DbSession.CreateObj(TypId);
    }

    [MemoryPackIgnore]
    public DbSession DbSession { get; set; } = null!;
    public Guid ObjId { get; set; }

    [MemoryPackIgnore]
    public DateTime CreatedAt
    {
        get => MemoryMarshal.Read<DateTime>(DbSession.GetFldValue(ObjId, Fields.CreatedAt));
        set => DbSession.SetFldValue(ObjId, Fields.CreatedAt, value.AsSpan());
    }

    [MemoryPackIgnore]
    public string Path
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Path));
        set => DbSession.SetFldValue(ObjId, Fields.Path, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public string Name
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Name));
        set => DbSession.SetFldValue(ObjId, Fields.Name, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public Folder? Parent
    {
        get => GeneratedCodeHelper.GetNullableAssoc<Folder>(DbSession, ObjId, Fields.Parent);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.Parent, value?.ObjId ?? Guid.Empty, FDMF.Testing.Shared.BusinessModelModel.Folder.Fields.Subfolders);
    }

    [MemoryPackIgnore]
    public AssocCollection<Folder> Subfolders => new(DbSession, ObjId, Fields.Subfolders, Folder.Fields.Parent);

    [MemoryPackIgnore]
    public AssocCollection<Document> Documents => new(DbSession, ObjId, Fields.Documents, Document.Fields.Folder);

    public static bool operator ==(Folder a, Folder b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(Folder a, Folder b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(Folder other) => this == other;
    public override bool Equals(object? obj) => obj is Folder other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///84070119-dce8-46a2-b181-bc98cc33df58
    public static Guid TypId { get; } = new Guid([25, 1, 7, 132, 232, 220, 162, 70, 177, 129, 188, 152, 204, 51, 223, 88]);

    public static class Fields
    {
        ///f9a1e438-70f0-48b9-bdb3-e4eb8c023e73
        public static readonly Guid CreatedAt = new Guid([56, 228, 161, 249, 240, 112, 185, 72, 189, 179, 228, 235, 140, 2, 62, 115]);
        ///aab2556c-adf6-47db-a6c3-53b6d80afb3d
        public static readonly Guid Path = new Guid([108, 85, 178, 170, 246, 173, 219, 71, 166, 195, 83, 182, 216, 10, 251, 61]);
        ///015b557a-cd86-41bb-8eb1-8cd4ce163dd9
        public static readonly Guid Name = new Guid([122, 85, 91, 1, 134, 205, 187, 65, 142, 177, 140, 212, 206, 22, 61, 217]);
        ///5f89cd1f-20ba-4e8f-945d-19df08f305a9
        public static readonly Guid Parent = new Guid([31, 205, 137, 95, 186, 32, 143, 78, 148, 93, 25, 223, 8, 243, 5, 169]);
        ///41ac6643-7a18-4878-b07a-8fe8d9851933
        public static readonly Guid Subfolders = new Guid([67, 102, 172, 65, 24, 122, 120, 72, 176, 122, 143, 232, 217, 133, 25, 51]);
        ///6a2ae56b-92f2-4201-ab74-bdb9a8515c2b
        public static readonly Guid Documents = new Guid([107, 229, 42, 106, 242, 146, 1, 66, 171, 116, 189, 185, 168, 81, 92, 43]);
    }
}
