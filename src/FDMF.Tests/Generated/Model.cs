// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using FDMF.Core;
using FDMF.Core.Database;
using MemoryPack;

namespace BaseModel.Generated;

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
    public AssocCollection<Model> ImportedBy => new(DbSession, ObjId, Fields.ImportedBy, Model.Fields.ImportedModels);
    [MemoryPackIgnore]
    public AssocCollection<EntityDefinition> EntityDefinitions => new(DbSession, ObjId, Fields.EntityDefinitions, EntityDefinition.Fields.Model);
    [MemoryPackIgnore]
    public AssocCollection<Model> ImportedModels => new(DbSession, ObjId, Fields.ImportedModels, Model.Fields.ImportedBy);

    public static bool operator ==(Model a, Model b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(Model a, Model b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(Model other) => this == other;
    public override bool Equals(object? obj) => obj is Model other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    public static Guid TypId { get; } = new Guid([13, 174, 57, 87, 121, 225, 70, 77, 186, 226, 8, 31, 153, 214, 153, 218]);

    public static class Fields
    {
        public static readonly Guid Name = new Guid([229, 254, 228, 239, 202, 187, 25, 75, 140, 38, 201, 107, 168, 178, 192, 8]);
        public static readonly Guid ImportedBy = new Guid([73, 190, 246, 191, 237, 106, 152, 73, 145, 171, 40, 112, 42, 62, 41, 176]);
        public static readonly Guid EntityDefinitions = new Guid([153, 139, 231, 6, 13, 75, 215, 69, 176, 200, 207, 87, 199, 63, 195, 232]);
        public static readonly Guid ImportedModels = new Guid([193, 112, 52, 202, 54, 207, 90, 65, 136, 193, 71, 194, 112, 15, 195, 125]);
    }
}
