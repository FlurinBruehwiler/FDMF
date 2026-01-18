// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using Shared;
using Shared.Database;
namespace Model.Generated;

[MemoryPackable]
public partial struct Model : ITransactionObject, IEquatable<Model>
{
    [Obsolete]
    [MemoryPackConstructor]
    public Model() { }
    public Model(DbSession dbSession)
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
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Name).AsSpan());
        set => DbSession.SetFldValue(ObjId, Fields.Name, Encoding.Unicode.GetBytes(value));
    }
    [MemoryPackIgnore]
    public AssocCollection<EntityDefinition> EntityDefinitions => new(DbSession, ObjId, Fields.EntityDefinitions, EntityDefinition.Fields.Model);

    public static bool operator ==(Model a, Model b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(Model a, Model b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(Model other) => this == other;
    public override bool Equals(object? obj) => obj is Model other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    public static Guid TypId { get; } = new Guid([165, 196, 112, 82, 224, 113, 116, 68, 159, 148, 80, 116, 91, 41, 180, 4]);

    public static class Fields
    {
        public static readonly Guid Name = new Guid([229, 254, 228, 239, 202, 187, 25, 75, 140, 38, 201, 107, 168, 178, 192, 8]);
        public static readonly Guid EntityDefinitions = new Guid([153, 139, 231, 6, 13, 75, 215, 69, 176, 200, 207, 87, 199, 63, 195, 232]);
    }
}
