// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

namespace FDMF.Testing.Shared.BusinessModelModel;

[MemoryPackable]
public partial struct Session : ITransactionObject, IEquatable<Session>
{
    [Obsolete]
    [MemoryPackConstructor]
    public Session() { }
    public Session(DbSession dbSession)
    {
        DbSession = dbSession;
        ObjId = DbSession.CreateObj(TypId);
    }

    [MemoryPackIgnore]
    public DbSession DbSession { get; set; } = null!;
    public Guid ObjId { get; set; }

    [MemoryPackIgnore]
    public bool IsPublic
    {
        get => MemoryMarshal.Read<bool>(DbSession.GetFldValue(ObjId, Fields.IsPublic));
        set => DbSession.SetFldValue(ObjId, Fields.IsPublic, value.AsSpan());
    }

    [MemoryPackIgnore]
    public DateTime StartAt
    {
        get => MemoryMarshal.Read<DateTime>(DbSession.GetFldValue(ObjId, Fields.StartAt));
        set => DbSession.SetFldValue(ObjId, Fields.StartAt, value.AsSpan());
    }

    [MemoryPackIgnore]
    public string Title
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Title));
        set => DbSession.SetFldValue(ObjId, Fields.Title, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public DateTime EndAt
    {
        get => MemoryMarshal.Read<DateTime>(DbSession.GetFldValue(ObjId, Fields.EndAt));
        set => DbSession.SetFldValue(ObjId, Fields.EndAt, value.AsSpan());
    }

    [MemoryPackIgnore]
    public AssocCollection<AgendaItem> AgendaItems => new(DbSession, ObjId, Fields.AgendaItems, AgendaItem.Fields.Session);

    [MemoryPackIgnore]
    public BusinessCase? BusinessCase
    {
        get => GeneratedCodeHelper.GetNullableAssoc<BusinessCase>(DbSession, ObjId, Fields.BusinessCase);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.BusinessCase, value?.ObjId ?? Guid.Empty, FDMF.Testing.Shared.BusinessModelModel.BusinessCase.Fields.Sessions);
    }

    public static bool operator ==(Session a, Session b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(Session a, Session b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(Session other) => this == other;
    public override bool Equals(object? obj) => obj is Session other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///3c498c35-ec98-4705-9c3e-442588838f4e
    public static Guid TypId { get; } = new Guid([53, 140, 73, 60, 152, 236, 5, 71, 156, 62, 68, 37, 136, 131, 143, 78]);

    public static class Fields
    {
        ///0364620f-0b33-441e-85fe-0272149b1a87
        public static readonly Guid IsPublic = new Guid([15, 98, 100, 3, 51, 11, 30, 68, 133, 254, 2, 114, 20, 155, 26, 135]);
        ///7adfd10f-1b04-464f-81e3-875fc5148f1d
        public static readonly Guid StartAt = new Guid([15, 209, 223, 122, 4, 27, 79, 70, 129, 227, 135, 95, 197, 20, 143, 29]);
        ///7c8e8231-f6ed-4738-9a02-c3855d262cde
        public static readonly Guid Title = new Guid([49, 130, 142, 124, 237, 246, 56, 71, 154, 2, 195, 133, 93, 38, 44, 222]);
        ///7b73f645-1d83-4632-9f2a-c1dc6f982e43
        public static readonly Guid EndAt = new Guid([69, 246, 115, 123, 131, 29, 50, 70, 159, 42, 193, 220, 111, 152, 46, 67]);
        ///9d376b51-f16b-4269-b08b-8c9e948237c2
        public static readonly Guid AgendaItems = new Guid([81, 107, 55, 157, 107, 241, 105, 66, 176, 139, 140, 158, 148, 130, 55, 194]);
        ///7aff7bdb-743e-4ff9-abdd-936839cc72d9
        public static readonly Guid BusinessCase = new Guid([219, 123, 255, 122, 62, 116, 249, 79, 171, 221, 147, 104, 57, 204, 114, 217]);
    }
}
