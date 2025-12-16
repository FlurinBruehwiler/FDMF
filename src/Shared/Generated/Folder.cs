using System.Text;
using MemoryPack;
using Shared.Database;

namespace Shared.Generated;

[MemoryPackable]
public partial struct Folder : ITransactionObject
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
    public DbSession DbSession { get; set; }
    public Guid ObjId { get; set; }

    [MemoryPackIgnore]
    public string Name
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Name).AsSpan());
        set => DbSession.SetFldValue(ObjId, Fields.Name, Encoding.Unicode.GetBytes(value).AsSpan().AsSlice());
    }
    [MemoryPackIgnore]
    public AssocCollection<Folder> Subfolders => new(DbSession, ObjId, Fields.Subfolders, Folder.Fields.Parent);
    [MemoryPackIgnore]
    public Folder? Parent
    {
        get => GeneratedCodeHelper.GetNullableAssoc<Folder>(DbSession, ObjId, Fields.Parent);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.Parent, value?.ObjId ?? Guid.Empty, Folder.Fields.Subfolders);
    }

    public static bool operator ==(Folder a, Folder b) => a.ObjId == b.ObjId;
    public static bool operator !=(Folder a, Folder b) => a.ObjId != b.ObjId;

    public static Guid TypId { get; } = new Guid([139, 189, 204, 163, 86, 34, 75, 65, 164, 2, 26, 9, 28, 180, 7, 165]);

    public static class Fields
    {
        public static readonly Guid Name = new Guid([123, 108, 105, 24, 93, 11, 106, 64, 159, 76, 242, 232, 48, 204, 15, 85]);
        public static readonly Guid Subfolders = new Guid([118, 212, 11, 180, 163, 217, 84, 69, 162, 246, 79, 119, 96, 192, 255, 228]);
        public static readonly Guid Parent = new Guid([167, 173, 160, 131, 83, 186, 119, 72, 130, 98, 189, 71, 82, 14, 234, 199]);
    }
}
