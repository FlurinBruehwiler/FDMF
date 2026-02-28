// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
#pragma warning disable CS0612 // Type or member is obsolete

namespace FDMF.Testing.Shared.BusinessModelModel;

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

    [MemoryPackIgnore]
    public string UserName
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.UserName));
        set => DbSession.SetFldValue(ObjId, Fields.UserName, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public bool IsActive
    {
        get => MemoryMarshal.Read<bool>(DbSession.GetFldValue(ObjId, Fields.IsActive));
        set => DbSession.SetFldValue(ObjId, Fields.IsActive, value.AsSpan());
    }

    [MemoryPackIgnore]
    public string Email
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Email));
        set => DbSession.SetFldValue(ObjId, Fields.Email, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public string DisplayName
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.DisplayName));
        set => DbSession.SetFldValue(ObjId, Fields.DisplayName, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public AssocCollection<Document> ExplicitlyViewableDocuments => new(DbSession, ObjId, Fields.ExplicitlyViewableDocuments, Document.Fields.ExplicitViewers);

    [MemoryPackIgnore]
    public AssocCollection<Document> CreatedDocuments => new(DbSession, ObjId, Fields.CreatedDocuments, Document.Fields.CreatedBy);

    [MemoryPackIgnore]
    public AssocCollection<BusinessCase> OwnedBusinessCases => new(DbSession, ObjId, Fields.OwnedBusinessCases, BusinessCase.Fields.Owners);

    [MemoryPackIgnore]
    public AssocCollection<OrganizationalUnit> MemberOfUnits => new(DbSession, ObjId, Fields.MemberOfUnits, OrganizationalUnit.Fields.Members);

    public static bool operator ==(User a, User b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(User a, User b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(User other) => this == other;
    public override bool Equals(object? obj) => obj is User other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///095433d0-4e74-471d-9b11-aea9538a86cc
    public static Guid TypId { get; } = new Guid([208, 51, 84, 9, 116, 78, 29, 71, 155, 17, 174, 169, 83, 138, 134, 204]);

    public static class Fields
    {
        ///105daf0f-6978-4dcb-bd64-a78a1a36b2c4
        public static readonly Guid UserName = new Guid([15, 175, 93, 16, 120, 105, 203, 77, 189, 100, 167, 138, 26, 54, 178, 196]);
        ///76440653-66af-4c24-b3f8-80f39afe52a6
        public static readonly Guid IsActive = new Guid([83, 6, 68, 118, 175, 102, 36, 76, 179, 248, 128, 243, 154, 254, 82, 166]);
        ///46135953-da64-4c2c-8fd6-b8618c11cec0
        public static readonly Guid Email = new Guid([83, 89, 19, 70, 100, 218, 44, 76, 143, 214, 184, 97, 140, 17, 206, 192]);
        ///46be95ae-0738-4c18-ae24-51a97b649a51
        public static readonly Guid DisplayName = new Guid([174, 149, 190, 70, 56, 7, 24, 76, 174, 36, 81, 169, 123, 100, 154, 81]);
        ///7c857219-3363-4183-9746-cca341d0c5f4
        public static readonly Guid ExplicitlyViewableDocuments = new Guid([25, 114, 133, 124, 99, 51, 131, 65, 151, 70, 204, 163, 65, 208, 197, 244]);
        ///d53ecf20-65c1-4f44-8e3a-fcec2bf26f52
        public static readonly Guid CreatedDocuments = new Guid([32, 207, 62, 213, 193, 101, 68, 79, 142, 58, 252, 236, 43, 242, 111, 82]);
        ///3ec3c42b-98ea-48dd-8835-01863be885a5
        public static readonly Guid OwnedBusinessCases = new Guid([43, 196, 195, 62, 234, 152, 221, 72, 136, 53, 1, 134, 59, 232, 133, 165]);
        ///ca5cc6fa-8006-42a0-a85d-8e1c081f9be6
        public static readonly Guid MemberOfUnits = new Guid([250, 198, 92, 202, 6, 128, 160, 66, 168, 93, 142, 28, 8, 31, 155, 230]);
    }
}
