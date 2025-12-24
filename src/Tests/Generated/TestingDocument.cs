// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using Shared;
using Shared.Database;
namespace TestModel.Generated;

[MemoryPackable]
public partial struct TestingDocument : ITransactionObject, IEquatable<TestingDocument>
{
    [Obsolete]
    [MemoryPackConstructor]
    public TestingDocument() { }
    public TestingDocument(DbSession dbSession)
    {
        DbSession = dbSession;
        ObjId = DbSession.CreateObj(TypId);
    }

    [MemoryPackIgnore]
    public DbSession DbSession { get; set; } = null!;
    public Guid ObjId { get; set; }


    public static bool operator ==(TestingDocument a, TestingDocument b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(TestingDocument a, TestingDocument b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(TestingDocument other) => this == other;
    public override bool Equals(object? obj) => obj is TestingDocument other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);

    public static Guid TypId { get; } = new Guid([186, 75, 24, 229, 112, 244, 171, 75, 174, 237, 40, 251, 144, 125, 163, 73]);

    public static class Fields
    {
    }
}
