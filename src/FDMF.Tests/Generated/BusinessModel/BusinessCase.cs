// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
namespace FDMF.Tests.BusinessModelModel;

[MemoryPackable]
public partial struct BusinessCase : ITransactionObject, IEquatable<BusinessCase>
{
    [Obsolete]
    [MemoryPackConstructor]
    public BusinessCase() { }
    public BusinessCase(DbSession dbSession)
    {
        DbSession = dbSession;
        ObjId = DbSession.CreateObj(TypId);
    }

    [MemoryPackIgnore]
    public DbSession DbSession { get; set; } = null!;
    public Guid ObjId { get; set; }

    [MemoryPackIgnore]
    public bool IsArchived
    {
        get => MemoryMarshal.Read<bool>(DbSession.GetFldValue(ObjId, Fields.IsArchived));
        set => DbSession.SetFldValue(ObjId, Fields.IsArchived, value.AsSpan());
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
    public string Number
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Number));
        set => DbSession.SetFldValue(ObjId, Fields.Number, Encoding.Unicode.GetBytes(value));
    }
    [MemoryPackIgnore]
    public string State
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.State));
        set => DbSession.SetFldValue(ObjId, Fields.State, Encoding.Unicode.GetBytes(value));
    }
    [MemoryPackIgnore]
    public OrganizationalUnit? OwnerUnit
    {
        get => GeneratedCodeHelper.GetNullableAssoc<OrganizationalUnit>(DbSession, ObjId, Fields.OwnerUnit);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.OwnerUnit, value?.ObjId ?? Guid.Empty, FDMF.Tests.BusinessModelModel.OrganizationalUnit.Fields.BusinessCases);
    }
    [MemoryPackIgnore]
    public AssocCollection<Document> Documents => new(DbSession, ObjId, Fields.Documents, Document.Fields.BusinessCase);
    [MemoryPackIgnore]
    public AssocCollection<Session> Sessions => new(DbSession, ObjId, Fields.Sessions, Session.Fields.BusinessCase);
    [MemoryPackIgnore]
    public AssocCollection<User> Owners => new(DbSession, ObjId, Fields.Owners, User.Fields.OwnedBusinessCases);

    public static bool operator ==(BusinessCase a, BusinessCase b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(BusinessCase a, BusinessCase b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(BusinessCase other) => this == other;
    public override bool Equals(object? obj) => obj is BusinessCase other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    //cd030f9b-24ed-4792-a48d-c187939cae85
    public static Guid TypId { get; } = new Guid([155, 15, 3, 205, 237, 36, 146, 71, 164, 141, 193, 135, 147, 156, 174, 133]);

    public static class Fields
    {
        //f2284a04-897d-44da-a3f2-ed06d75f67b0
        public static readonly Guid IsArchived = new Guid([4, 74, 40, 242, 125, 137, 218, 68, 163, 242, 237, 6, 215, 95, 103, 176]);
        //2a743537-6e95-4ec3-8422-be79838b7bd5
        public static readonly Guid Title = new Guid([55, 53, 116, 42, 149, 110, 195, 78, 132, 34, 190, 121, 131, 139, 123, 213]);
        //b51f1c43-9fb0-4e79-99bd-041a69e95346
        public static readonly Guid CreatedAt = new Guid([67, 28, 31, 181, 176, 159, 121, 78, 153, 189, 4, 26, 105, 233, 83, 70]);
        //0c52f663-a159-4e40-91e4-f7327b13e793
        public static readonly Guid Locked = new Guid([99, 246, 82, 12, 89, 161, 64, 78, 145, 228, 247, 50, 123, 19, 231, 147]);
        //24c95c72-2331-4b22-ad50-d6924265f009
        public static readonly Guid Number = new Guid([114, 92, 201, 36, 49, 35, 34, 75, 173, 80, 214, 146, 66, 101, 240, 9]);
        //d1576aac-1e5e-4832-93ed-d912b4911f02
        public static readonly Guid State = new Guid([172, 106, 87, 209, 94, 30, 50, 72, 147, 237, 217, 18, 180, 145, 31, 2]);
        //7ced3833-3857-4846-9ffb-b2acea9fb9f3
        public static readonly Guid OwnerUnit = new Guid([51, 56, 237, 124, 87, 56, 70, 72, 159, 251, 178, 172, 234, 159, 185, 243]);
        //6bc1dd86-abde-4108-9e94-48c4ec85b6bf
        public static readonly Guid Documents = new Guid([134, 221, 193, 107, 222, 171, 8, 65, 158, 148, 72, 196, 236, 133, 182, 191]);
        //5163b291-34a1-4849-85ff-3c71d356b43f
        public static readonly Guid Sessions = new Guid([145, 178, 99, 81, 161, 52, 73, 72, 133, 255, 60, 113, 211, 86, 180, 63]);
        //6af1eef1-4b48-478e-b3f4-d2634edb0f7a
        public static readonly Guid Owners = new Guid([241, 238, 241, 106, 72, 75, 142, 71, 179, 244, 210, 99, 78, 219, 15, 122]);
    }
}
