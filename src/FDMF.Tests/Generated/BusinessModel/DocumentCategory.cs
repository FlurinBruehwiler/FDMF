// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

namespace FDMF.Tests.BusinessModelModel;

[MemoryPackable]
public partial struct DocumentCategory : ITransactionObject, IEquatable<DocumentCategory>
{
    [Obsolete]
    [MemoryPackConstructor]
    public DocumentCategory() { }
    public DocumentCategory(DbSession dbSession)
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
    public string Key
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Key));
        set => DbSession.SetFldValue(ObjId, Fields.Key, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public long SortOrder
    {
        get => MemoryMarshal.Read<long>(DbSession.GetFldValue(ObjId, Fields.SortOrder));
        set => DbSession.SetFldValue(ObjId, Fields.SortOrder, value.AsSpan());
    }

    [MemoryPackIgnore]
    public bool IsConfidentialDefault
    {
        get => MemoryMarshal.Read<bool>(DbSession.GetFldValue(ObjId, Fields.IsConfidentialDefault));
        set => DbSession.SetFldValue(ObjId, Fields.IsConfidentialDefault, value.AsSpan());
    }

    [MemoryPackIgnore]
    public AssocCollection<Document> Documents => new(DbSession, ObjId, Fields.Documents, Document.Fields.Category);

    public static bool operator ==(DocumentCategory a, DocumentCategory b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(DocumentCategory a, DocumentCategory b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(DocumentCategory other) => this == other;
    public override bool Equals(object? obj) => obj is DocumentCategory other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///8a047a4b-d907-46c7-8d27-1c8b04ceae77
    public static Guid TypId { get; } = new Guid([75, 122, 4, 138, 7, 217, 199, 70, 141, 39, 28, 139, 4, 206, 174, 119]);

    public static class Fields
    {
        ///8e2ba669-3ebd-4758-8d71-c0de543da922
        public static readonly Guid Name = new Guid([105, 166, 43, 142, 189, 62, 88, 71, 141, 113, 192, 222, 84, 61, 169, 34]);
        ///b71be27a-717c-4866-9452-b72d06a325e9
        public static readonly Guid Key = new Guid([122, 226, 27, 183, 124, 113, 102, 72, 148, 82, 183, 45, 6, 163, 37, 233]);
        ///63e373d8-95be-4c3a-a481-038a057ae3bf
        public static readonly Guid SortOrder = new Guid([216, 115, 227, 99, 190, 149, 58, 76, 164, 129, 3, 138, 5, 122, 227, 191]);
        ///ff7545ed-394a-4c9f-b73b-c597120e021a
        public static readonly Guid IsConfidentialDefault = new Guid([237, 69, 117, 255, 74, 57, 159, 76, 183, 59, 197, 151, 18, 14, 2, 26]);
        ///3115abc8-17d2-4542-a085-78edbb3b2ab7
        public static readonly Guid Documents = new Guid([200, 171, 21, 49, 210, 23, 66, 69, 160, 133, 120, 237, 187, 59, 42, 183]);
    }
}
