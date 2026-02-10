// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

namespace FDMF.Testing.Shared.BusinessModelModel;

[MemoryPackable]
public partial struct AgendaItem : ITransactionObject, IEquatable<AgendaItem>
{
    [Obsolete]
    [MemoryPackConstructor]
    public AgendaItem() { }
    public AgendaItem(DbSession dbSession)
    {
        DbSession = dbSession;
        ObjId = DbSession.CreateObj(TypId);
    }

    [MemoryPackIgnore]
    public DbSession DbSession { get; set; } = null!;
    public Guid ObjId { get; set; }

    [MemoryPackIgnore]
    public long Position
    {
        get => MemoryMarshal.Read<long>(DbSession.GetFldValue(ObjId, Fields.Position));
        set => DbSession.SetFldValue(ObjId, Fields.Position, value.AsSpan());
    }

    [MemoryPackIgnore]
    public string Title
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Title));
        set => DbSession.SetFldValue(ObjId, Fields.Title, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public bool IsConfidential
    {
        get => MemoryMarshal.Read<bool>(DbSession.GetFldValue(ObjId, Fields.IsConfidential));
        set => DbSession.SetFldValue(ObjId, Fields.IsConfidential, value.AsSpan());
    }

    [MemoryPackIgnore]
    public string State
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.State));
        set => DbSession.SetFldValue(ObjId, Fields.State, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public Session? Session
    {
        get => GeneratedCodeHelper.GetNullableAssoc<Session>(DbSession, ObjId, Fields.Session);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.Session, value?.ObjId ?? Guid.Empty, FDMF.Testing.Shared.BusinessModelModel.Session.Fields.AgendaItems);
    }

    [MemoryPackIgnore]
    public AssocCollection<Document> Documents => new(DbSession, ObjId, Fields.Documents, Document.Fields.AgendaItems);

    public static bool operator ==(AgendaItem a, AgendaItem b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(AgendaItem a, AgendaItem b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(AgendaItem other) => this == other;
    public override bool Equals(object? obj) => obj is AgendaItem other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///30561d20-43d6-435d-a5db-9f151b3ec970
    public static Guid TypId { get; } = new Guid([32, 29, 86, 48, 214, 67, 93, 67, 165, 219, 159, 21, 27, 62, 201, 112]);

    public static class Fields
    {
        ///1199121d-bc49-4963-9327-f3f6855fe4b3
        public static readonly Guid Position = new Guid([29, 18, 153, 17, 73, 188, 99, 73, 147, 39, 243, 246, 133, 95, 228, 179]);
        ///9fc27b47-1962-4fa0-aca2-a7eb970aa22d
        public static readonly Guid Title = new Guid([71, 123, 194, 159, 98, 25, 160, 79, 172, 162, 167, 235, 151, 10, 162, 45]);
        ///2cf80089-0848-47d3-b940-680440beb671
        public static readonly Guid IsConfidential = new Guid([137, 0, 248, 44, 72, 8, 211, 71, 185, 64, 104, 4, 64, 190, 182, 113]);
        ///9afad796-18ca-44ab-a0ad-2b2a84c03de7
        public static readonly Guid State = new Guid([150, 215, 250, 154, 202, 24, 171, 68, 160, 173, 43, 42, 132, 192, 61, 231]);
        ///838b1b29-52b3-4395-869a-13a00a04fe74
        public static readonly Guid Session = new Guid([41, 27, 139, 131, 179, 82, 149, 67, 134, 154, 19, 160, 10, 4, 254, 116]);
        ///a8a91cbb-255b-4e27-835b-b0f5896d6b9d
        public static readonly Guid Documents = new Guid([187, 28, 169, 168, 91, 37, 39, 78, 131, 91, 176, 245, 137, 109, 107, 157]);
    }
}
