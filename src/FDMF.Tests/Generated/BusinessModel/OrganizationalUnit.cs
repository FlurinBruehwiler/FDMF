// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
namespace FDMF.Tests.BusinessModelModel;

[MemoryPackable]
public partial struct OrganizationalUnit : ITransactionObject, IEquatable<OrganizationalUnit>
{
    [Obsolete]
    [MemoryPackConstructor]
    public OrganizationalUnit() { }
    public OrganizationalUnit(DbSession dbSession)
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
    public string Code
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Code));
        set => DbSession.SetFldValue(ObjId, Fields.Code, Encoding.Unicode.GetBytes(value));
    }
    [MemoryPackIgnore]
    public OrganizationalUnit? Parent
    {
        get => GeneratedCodeHelper.GetNullableAssoc<OrganizationalUnit>(DbSession, ObjId, Fields.Parent);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.Parent, value?.ObjId ?? Guid.Empty, FDMF.Tests.BusinessModelModel.OrganizationalUnit.Fields.Children);
    }
    [MemoryPackIgnore]
    public AssocCollection<User> Members => new(DbSession, ObjId, Fields.Members, User.Fields.MemberOfUnits);
    [MemoryPackIgnore]
    public AssocCollection<OrganizationalUnit> Children => new(DbSession, ObjId, Fields.Children, OrganizationalUnit.Fields.Parent);
    [MemoryPackIgnore]
    public AssocCollection<Document> Documents => new(DbSession, ObjId, Fields.Documents, Document.Fields.OwnerUnit);
    [MemoryPackIgnore]
    public AssocCollection<BusinessCase> BusinessCases => new(DbSession, ObjId, Fields.BusinessCases, BusinessCase.Fields.OwnerUnit);

    public static bool operator ==(OrganizationalUnit a, OrganizationalUnit b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(OrganizationalUnit a, OrganizationalUnit b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(OrganizationalUnit other) => this == other;
    public override bool Equals(object? obj) => obj is OrganizationalUnit other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    //81daacd7-4b3f-44bf-b73c-52c7012ad758
    public static Guid TypId { get; } = new Guid([215, 172, 218, 129, 63, 75, 191, 68, 183, 60, 82, 199, 1, 42, 215, 88]);

    public static class Fields
    {
        //88bc11c2-903d-4517-b079-4f3fc5ce20b9
        public static readonly Guid Name = new Guid([194, 17, 188, 136, 61, 144, 23, 69, 176, 121, 79, 63, 197, 206, 32, 185]);
        //26d197fc-716e-4377-83a0-93e134f33b8c
        public static readonly Guid Code = new Guid([252, 151, 209, 38, 110, 113, 119, 67, 131, 160, 147, 225, 52, 243, 59, 140]);
        //77e01c0c-f7a4-47e8-b996-3790d2f601f6
        public static readonly Guid Parent = new Guid([12, 28, 224, 119, 164, 247, 232, 71, 185, 150, 55, 144, 210, 246, 1, 246]);
        //f86ce70e-57df-4056-8ff2-9cc0f6af444e
        public static readonly Guid Members = new Guid([14, 231, 108, 248, 223, 87, 86, 64, 143, 242, 156, 192, 246, 175, 68, 78]);
        //c1e8b920-cd17-40b1-8785-ea2d9ca84b59
        public static readonly Guid Children = new Guid([32, 185, 232, 193, 23, 205, 177, 64, 135, 133, 234, 45, 156, 168, 75, 89]);
        //2157a52c-8270-45ed-a05d-5b387829b376
        public static readonly Guid Documents = new Guid([44, 165, 87, 33, 112, 130, 237, 69, 160, 93, 91, 56, 120, 41, 179, 118]);
        //6b06a7d3-2c1a-4482-a7f0-271154f15229
        public static readonly Guid BusinessCases = new Guid([211, 167, 6, 107, 26, 44, 130, 68, 167, 240, 39, 17, 84, 241, 82, 41]);
    }
}
