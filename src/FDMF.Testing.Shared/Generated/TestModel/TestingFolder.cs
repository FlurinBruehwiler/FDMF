// ReSharper disable All
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;

namespace FDMF.Testing.Shared.TestModelModel;

[MemoryPackable]
public partial struct TestingFolder : ITransactionObject, IEquatable<TestingFolder>
{
    [Obsolete]
    [MemoryPackConstructor]
    public TestingFolder() { }
    public TestingFolder(DbSession dbSession)
    {
        DbSession = dbSession;
        ObjId = DbSession.CreateObj(TypId);
    }

    [MemoryPackIgnore]
    public DbSession DbSession { get; set; } = null!;
    public Guid ObjId { get; set; }

    [MemoryPackIgnore]
    public bool TestBoolField
    {
        get => MemoryMarshal.Read<bool>(DbSession.GetFldValue(ObjId, Fields.TestBoolField));
        set => DbSession.SetFldValue(ObjId, Fields.TestBoolField, value.AsSpan());
    }

    [MemoryPackIgnore]
    public DateTime TestDateField
    {
        get => MemoryMarshal.Read<DateTime>(DbSession.GetFldValue(ObjId, Fields.TestDateField));
        set => DbSession.SetFldValue(ObjId, Fields.TestDateField, value.AsSpan());
    }

    [MemoryPackIgnore]
    public decimal TestDecimalField
    {
        get => MemoryMarshal.Read<decimal>(DbSession.GetFldValue(ObjId, Fields.TestDecimalField));
        set => DbSession.SetFldValue(ObjId, Fields.TestDecimalField, value.AsSpan());
    }

    [MemoryPackIgnore]
    public string Name
    {
        get => Encoding.Unicode.GetString(DbSession.GetFldValue(ObjId, Fields.Name));
        set => DbSession.SetFldValue(ObjId, Fields.Name, Encoding.Unicode.GetBytes(value));
    }

    [MemoryPackIgnore]
    public long TestIntegerField
    {
        get => MemoryMarshal.Read<long>(DbSession.GetFldValue(ObjId, Fields.TestIntegerField));
        set => DbSession.SetFldValue(ObjId, Fields.TestIntegerField, value.AsSpan());
    }

    [MemoryPackIgnore]
    public AssocCollection<TestingFolder> Subfolders => new(DbSession, ObjId, Fields.Subfolders, TestingFolder.Fields.Parent);

    [MemoryPackIgnore]
    public TestingFolder? Parent
    {
        get => GeneratedCodeHelper.GetNullableAssoc<TestingFolder>(DbSession, ObjId, Fields.Parent);
        set => GeneratedCodeHelper.SetAssoc(DbSession, ObjId, Fields.Parent, value?.ObjId ?? Guid.Empty, FDMF.Testing.Shared.TestModelModel.TestingFolder.Fields.Subfolders);
    }

    public static bool operator ==(TestingFolder a, TestingFolder b) => a.DbSession == b.DbSession && a.ObjId == b.ObjId;
    public static bool operator !=(TestingFolder a, TestingFolder b) => a.DbSession != b.DbSession || a.ObjId != b.ObjId;
    public bool Equals(TestingFolder other) => this == other;
    public override bool Equals(object? obj) => obj is TestingFolder other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DbSession, ObjId);
    public override string ToString() => ObjId.ToString();

    ///a3ccbd8b-2256-414b-a402-1a091cb407a5
    public static Guid TypId { get; } = new Guid([139, 189, 204, 163, 86, 34, 75, 65, 164, 2, 26, 9, 28, 180, 7, 165]);

    public static class Fields
    {
        ///d2f1f644-4a30-4f6e-9a3c-39d9e7b6a1e2
        public static readonly Guid TestBoolField = new Guid([68, 246, 241, 210, 48, 74, 110, 79, 154, 60, 57, 217, 231, 182, 161, 226]);
        ///19afb14d-e6e6-4ffa-947b-b4b861d0118e
        public static readonly Guid TestDateField = new Guid([77, 177, 175, 25, 230, 230, 250, 79, 148, 123, 180, 184, 97, 208, 17, 142]);
        ///25b2926a-e77d-4a1f-ba75-9d3c76ac7dd7
        public static readonly Guid TestDecimalField = new Guid([106, 146, 178, 37, 125, 231, 31, 74, 186, 117, 157, 60, 118, 172, 125, 215]);
        ///18696c7b-0b5d-406a-9f4c-f2e830cc0f55
        public static readonly Guid Name = new Guid([123, 108, 105, 24, 93, 11, 106, 64, 159, 76, 242, 232, 48, 204, 15, 85]);
        ///71d87fdd-7415-49cd-9204-44d43073df9d
        public static readonly Guid TestIntegerField = new Guid([221, 127, 216, 113, 21, 116, 205, 73, 146, 4, 68, 212, 48, 115, 223, 157]);
        ///b40bd476-d9a3-4554-a2f6-4f7760c0ffe4
        public static readonly Guid Subfolders = new Guid([118, 212, 11, 180, 163, 217, 84, 69, 162, 246, 79, 119, 96, 192, 255, 228]);
        ///83a0ada7-ba53-4877-8262-bd47520eeac7
        public static readonly Guid Parent = new Guid([167, 173, 160, 131, 83, 186, 119, 72, 130, 98, 189, 71, 82, 14, 234, 199]);
    }
}
