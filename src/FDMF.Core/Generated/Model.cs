// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
#pragma warning disable CS0612 // Type or member is obsolete

namespace FDMF.Core.DatabaseLayer;

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
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Name));
        set => DbSession.SetFldValue(ObjId, Fields.Name, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public AssocCollection<Model> ImportedBy => new(DbSession, ObjId, Fields.ImportedBy, Model.Fields.ImportedModels);

    [MemoryPackIgnore]
    public AssocCollection<EntityDefinition> EntityDefinitions => new(DbSession, ObjId, Fields.EntityDefinitions, EntityDefinition.Fields.Model);

    [MemoryPackIgnore]
    public AssocCollection<Model> ImportedModels => new(DbSession, ObjId, Fields.ImportedModels, Model.Fields.ImportedBy);


    public static implicit operator RootEntity(Model value) => new RootEntity { DbSession = value.DbSession, ObjId = value.ObjId };

    public static explicit operator Model(RootEntity value)
    {
        var actual = value.DbSession.GetTypId(value.ObjId);
        if (!GeneratedCodeHelper.IsAssignableFrom(value.DbSession, TypId, actual))
            throw new System.InvalidCastException("Cannot cast 'RootEntity' to 'Model'");
        return new Model { DbSession = value.DbSession, ObjId = value.ObjId };
    }

    public static bool TryCastFrom(RootEntity value, out Model result)
    {
        var actual = value.DbSession.GetTypId(value.ObjId);
        if (GeneratedCodeHelper.IsAssignableFrom(value.DbSession, TypId, actual))
        {
            result = new Model { DbSession = value.DbSession, ObjId = value.ObjId };
            return true;
        }
        result = default;
        return false;
    }

    public static bool operator ==(Model a, Model b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(Model a, Model b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(Model other) => this == other;
    public override bool Equals(object? obj) => obj is Model other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///5739ae0d-e179-4d46-bae2-081f99d699da
    public static Guid TypId { get; } = new Guid([13, 174, 57, 87, 121, 225, 70, 77, 186, 226, 8, 31, 153, 214, 153, 218]);

    public static class Fields
    {
        ///efe4fee5-bbca-4b19-8c26-c96ba8b2c008
        public static readonly Guid Name = new Guid([229, 254, 228, 239, 202, 187, 25, 75, 140, 38, 201, 107, 168, 178, 192, 8]);
        ///bff6be49-6aed-4998-91ab-28702a3e29b0
        public static readonly Guid ImportedBy = new Guid([73, 190, 246, 191, 237, 106, 152, 73, 145, 171, 40, 112, 42, 62, 41, 176]);
        ///06e78b99-4b0d-45d7-b0c8-cf57c73fc3e8
        public static readonly Guid EntityDefinitions = new Guid([153, 139, 231, 6, 13, 75, 215, 69, 176, 200, 207, 87, 199, 63, 195, 232]);
        ///ca3470c1-cf36-415a-88c1-47c2700fc37d
        public static readonly Guid ImportedModels = new Guid([193, 112, 52, 202, 54, 207, 90, 65, 136, 193, 71, 194, 112, 15, 195, 125]);
    }
}
