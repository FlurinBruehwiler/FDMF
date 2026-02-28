// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
#pragma warning disable CS0612 // Type or member is obsolete

namespace FDMF.Testing.Shared.BusinessModelModel;

[MemoryPackable]
public partial struct PdfDocument : ITransactionObject, IEquatable<PdfDocument>
{
    [Obsolete]
    [MemoryPackConstructor]
    public PdfDocument() { }
    public PdfDocument(DbSession dbSession)
    {
        DbSession = dbSession;
        ObjId = DbSession.CreateObj(TypId);
    }

    [MemoryPackIgnore]
    public DbSession DbSession { get; set; } = null!;
    public Guid ObjId { get; set; }

    [MemoryPackIgnore]
    public long FileSize
    {
        get => MemoryMarshal.Read<long>(DbSession.GetFldValue(ObjId, Fields.FileSize));
        set => DbSession.SetFldValue(ObjId, Fields.FileSize, value.AsSpan());
    }

    [MemoryPackIgnore]
    public string FileName
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.FileName));
        set => DbSession.SetFldValue(ObjId, Fields.FileName, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public string State
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.State));
        set => DbSession.SetFldValue(ObjId, Fields.State, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public string Title
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Title));
        set => DbSession.SetFldValue(ObjId, Fields.Title, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public DateTime CreatedAt
    {
        get => MemoryMarshal.Read<DateTime>(DbSession.GetFldValue(ObjId, Fields.CreatedAt));
        set => DbSession.SetFldValue(ObjId, Fields.CreatedAt, value.AsSpan());
    }

    [MemoryPackIgnore]
    public bool Locked
    {
        get => MemoryMarshal.Read<bool>(DbSession.GetFldValue(ObjId, Fields.Locked));
        set => DbSession.SetFldValue(ObjId, Fields.Locked, value.AsSpan());
    }

    [MemoryPackIgnore]
    public long PageCount
    {
        get => MemoryMarshal.Read<long>(DbSession.GetFldValue(ObjId, Fields.PageCount));
        set => DbSession.SetFldValue(ObjId, Fields.PageCount, value.AsSpan());
    }

    [MemoryPackIgnore]
    public Folder? Folder
    {
        get => GeneratedCodeHelper.GetNullableAssoc<Folder>(DbSession, ObjId, Fields.Folder);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.Folder, value?.ObjId ?? Guid.Empty, FDMF.Testing.Shared.BusinessModelModel.Folder.Fields.Documents);
    }

    [MemoryPackIgnore]
    public OrganizationalUnit? OwnerUnit
    {
        get => GeneratedCodeHelper.GetNullableAssoc<OrganizationalUnit>(DbSession, ObjId, Fields.OwnerUnit);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.OwnerUnit, value?.ObjId ?? Guid.Empty, FDMF.Testing.Shared.BusinessModelModel.OrganizationalUnit.Fields.Documents);
    }

    [MemoryPackIgnore]
    public DocumentCategory? Category
    {
        get => GeneratedCodeHelper.GetNullableAssoc<DocumentCategory>(DbSession, ObjId, Fields.Category);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.Category, value?.ObjId ?? Guid.Empty, FDMF.Testing.Shared.BusinessModelModel.DocumentCategory.Fields.Documents);
    }

    [MemoryPackIgnore]
    public User? CreatedBy
    {
        get => GeneratedCodeHelper.GetNullableAssoc<User>(DbSession, ObjId, Fields.CreatedBy);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.CreatedBy, value?.ObjId ?? Guid.Empty, FDMF.Testing.Shared.BusinessModelModel.User.Fields.CreatedDocuments);
    }

    [MemoryPackIgnore]
    public AssocCollection<AgendaItem> AgendaItems => new(DbSession, ObjId, Fields.AgendaItems, AgendaItem.Fields.Documents);

    [MemoryPackIgnore]
    public BusinessCase? BusinessCase
    {
        get => GeneratedCodeHelper.GetNullableAssoc<BusinessCase>(DbSession, ObjId, Fields.BusinessCase);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.BusinessCase, value?.ObjId ?? Guid.Empty, FDMF.Testing.Shared.BusinessModelModel.BusinessCase.Fields.Documents);
    }

    [MemoryPackIgnore]
    public AssocCollection<User> ExplicitViewers => new(DbSession, ObjId, Fields.ExplicitViewers, User.Fields.ExplicitlyViewableDocuments);


    public static implicit operator Document(PdfDocument value) => new Document { DbSession = value.DbSession, ObjId = value.ObjId };

    public static explicit operator PdfDocument(Document value)
    {
        var actual = value.DbSession.GetTypId(value.ObjId);
        if (!GeneratedCodeHelper.IsAssignableFrom(value.DbSession, TypId, actual))
            throw new System.InvalidCastException("Cannot cast 'Document' to 'PdfDocument'");
        return new PdfDocument { DbSession = value.DbSession, ObjId = value.ObjId };
    }

    public static bool TryCastFrom(Document value, out PdfDocument result)
    {
        var actual = value.DbSession.GetTypId(value.ObjId);
        if (GeneratedCodeHelper.IsAssignableFrom(value.DbSession, TypId, actual))
        {
            result = new PdfDocument { DbSession = value.DbSession, ObjId = value.ObjId };
            return true;
        }
        result = default;
        return false;
    }

    public static bool operator ==(PdfDocument a, PdfDocument b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(PdfDocument a, PdfDocument b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(PdfDocument other) => this == other;
    public override bool Equals(object? obj) => obj is PdfDocument other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///e0fb19a6-a512-477b-8f30-2152ff7cf9c0
    public static Guid TypId { get; } = new Guid([166, 25, 251, 224, 18, 165, 123, 71, 143, 48, 33, 82, 255, 124, 249, 192]);

    public static class Fields
    {
        ///885cd140-64f5-478f-a42c-2bea6aceaf3b
        public static readonly Guid FileSize = new Guid([64, 209, 92, 136, 245, 100, 143, 71, 164, 44, 43, 234, 106, 206, 175, 59]);
        ///a978a147-ab06-40e0-86b6-268f7f7fb3c1
        public static readonly Guid FileName = new Guid([71, 161, 120, 169, 6, 171, 224, 64, 134, 182, 38, 143, 127, 127, 179, 193]);
        ///e9783281-dcd3-4031-a5ed-a1ed13bbad24
        public static readonly Guid State = new Guid([129, 50, 120, 233, 211, 220, 49, 64, 165, 237, 161, 237, 19, 187, 173, 36]);
        ///3d50a185-4c99-43ab-9e18-0398aedbfa49
        public static readonly Guid Title = new Guid([133, 161, 80, 61, 153, 76, 171, 67, 158, 24, 3, 152, 174, 219, 250, 73]);
        ///542d76bd-5a9d-450c-8eda-d4a76eba4b58
        public static readonly Guid CreatedAt = new Guid([189, 118, 45, 84, 157, 90, 12, 69, 142, 218, 212, 167, 110, 186, 75, 88]);
        ///a9b59ad3-d030-49f6-b939-159d78d27461
        public static readonly Guid Locked = new Guid([211, 154, 181, 169, 48, 208, 246, 73, 185, 57, 21, 157, 120, 210, 116, 97]);
        ///1223cdc4-23ed-424e-a3cf-29d167702954
        public static readonly Guid PageCount = new Guid([196, 205, 35, 18, 237, 35, 78, 66, 163, 207, 41, 209, 103, 112, 41, 84]);
        ///5d7c1847-2984-4c4d-9a08-b2aa0e772bb1
        public static readonly Guid Folder = new Guid([71, 24, 124, 93, 132, 41, 77, 76, 154, 8, 178, 170, 14, 119, 43, 177]);
        ///35cfa966-9d9f-434f-95a7-507bb313f26f
        public static readonly Guid OwnerUnit = new Guid([102, 169, 207, 53, 159, 157, 79, 67, 149, 167, 80, 123, 179, 19, 242, 111]);
        ///25627768-6f43-4230-86bb-ffc1c57446be
        public static readonly Guid Category = new Guid([104, 119, 98, 37, 67, 111, 48, 66, 134, 187, 255, 193, 197, 116, 70, 190]);
        ///cc315293-3eac-476a-8480-de499673f608
        public static readonly Guid CreatedBy = new Guid([147, 82, 49, 204, 172, 62, 106, 71, 132, 128, 222, 73, 150, 115, 246, 8]);
        ///637a1b99-0ccd-4eb4-9915-630170bf03c5
        public static readonly Guid AgendaItems = new Guid([153, 27, 122, 99, 205, 12, 180, 78, 153, 21, 99, 1, 112, 191, 3, 197]);
        ///2df0f6aa-582d-4daf-89a1-def70bf75aed
        public static readonly Guid BusinessCase = new Guid([170, 246, 240, 45, 45, 88, 175, 77, 137, 161, 222, 247, 11, 247, 90, 237]);
        ///3f0771ed-7629-495c-bff2-5b8d3939f169
        public static readonly Guid ExplicitViewers = new Guid([237, 113, 7, 63, 41, 118, 92, 73, 191, 242, 91, 141, 57, 57, 241, 105]);
    }
}
