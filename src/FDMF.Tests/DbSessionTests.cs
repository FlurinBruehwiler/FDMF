using System.Text;
using FDMF.Core.DatabaseLayer;
using FDMF.Tests.TestModelModel;
using Environment = FDMF.Core.Environment;

namespace FDMF.Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public sealed class DbSessionTests
{
    [Fact]
    public void DeleteObj_Removes_Obj_And_All_Values()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());
        using var session = new DbSession(env);

        var objId = session.CreateObj(TestingFolder.TypId);
        session.SetFldValue(objId, TestingFolder.Fields.Name, Encoding.Unicode.GetBytes("abc"));
        long i = 123;
        session.SetFldValue(objId, TestingFolder.Fields.TestIntegerField, i.AsSpan());

        session.DeleteObj(objId);

        Assert.Equal(Guid.Empty, session.GetTypId(objId));
        Assert.Equal(0, session.GetFldValue(objId, TestingFolder.Fields.Name).Length);
        Assert.Equal(0, session.GetFldValue(objId, TestingFolder.Fields.TestIntegerField).Length);
    }

    [Fact]
    public void DeleteObj_Removes_Associations_On_Both_Sides()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());
        using var session = new DbSession(env);

        var a = session.CreateObj(TestingFolder.TypId);
        var b = session.CreateObj(TestingFolder.TypId);

        // a.Parent -> b (and therefore b.Subfolders contains a)
        session.CreateAso(a, TestingFolder.Fields.Parent, b, TestingFolder.Fields.Subfolders);

        Assert.Equal(1, session.GetAsoCount(a, TestingFolder.Fields.Parent));
        Assert.Equal(1, session.GetAsoCount(b, TestingFolder.Fields.Subfolders));

        session.DeleteObj(a);

        Assert.Equal(0, session.GetAsoCount(b, TestingFolder.Fields.Subfolders));
        Assert.Equal(Guid.Empty, session.GetTypId(a));
    }

    [Fact]
    public void RemoveAllAso_Removes_All_Associations_And_All_Opposites()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());
        using var session = new DbSession(env);

        var parent = session.CreateObj(TestingFolder.TypId);
        var childA = session.CreateObj(TestingFolder.TypId);
        var childB = session.CreateObj(TestingFolder.TypId);

        session.CreateAso(childA, TestingFolder.Fields.Parent, parent, TestingFolder.Fields.Subfolders);
        session.CreateAso(childB, TestingFolder.Fields.Parent, parent, TestingFolder.Fields.Subfolders);

        Assert.Equal(2, session.GetAsoCount(parent, TestingFolder.Fields.Subfolders));

        session.RemoveAllAso(parent, TestingFolder.Fields.Subfolders);

        Assert.Equal(0, session.GetAsoCount(parent, TestingFolder.Fields.Subfolders));
        Assert.Equal(0, session.GetAsoCount(childA, TestingFolder.Fields.Parent));
        Assert.Equal(0, session.GetAsoCount(childB, TestingFolder.Fields.Parent));
    }

    [Fact]
    public void CreateObj_With_FixedId_Can_Be_Loaded_And_Deleted()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());
        using var session = new DbSession(env);

        var id = Guid.NewGuid();
        var created = session.CreateObj(TestingFolder.TypId, fixedId: id);
        Assert.Equal(id, created);
        Assert.Equal(TestingFolder.TypId, session.GetTypId(id));

        session.DeleteObj(id);
        Assert.Equal(Guid.Empty, session.GetTypId(id));
    }

    [Fact]
    public void TryGetObjFromGuid_Returns_False_For_Wrong_Type()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());
        using var session = new DbSession(env);

        var id = session.CreateObj(TestingFolder.TypId);

        Assert.False(session.TryGetObjFromGuid<TestingDocument>(id, out _));
        Assert.True(session.TryGetObjFromGuid<TestingFolder>(id, out var folder));
        Assert.Equal(id, folder.ObjId);
    }

    [Fact]
    public void EnumerateAso_Enumerates_All_Associations_For_Field()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());
        using var session = new DbSession(env);

        var parent = session.CreateObj(TestingFolder.TypId);
        var childA = session.CreateObj(TestingFolder.TypId);
        var childB = session.CreateObj(TestingFolder.TypId);

        session.CreateAso(childA, TestingFolder.Fields.Parent, parent, TestingFolder.Fields.Subfolders);
        session.CreateAso(childB, TestingFolder.Fields.Parent, parent, TestingFolder.Fields.Subfolders);

        var ids = session.EnumerateAso(parent, TestingFolder.Fields.Subfolders)
            .Select(x => x.ObjId)
            .ToHashSet();

        Assert.Equal(new HashSet<Guid> { childA, childB }, ids);
    }

    [Fact]
    public void GetSingleAsoValue_Returns_Null_When_Empty_And_Value_When_Set()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());
        using var session = new DbSession(env);

        var parent = session.CreateObj(TestingFolder.TypId);
        var child = session.CreateObj(TestingFolder.TypId);

        Assert.Null(session.GetSingleAsoValue(child, TestingFolder.Fields.Parent));

        session.CreateAso(child, TestingFolder.Fields.Parent, parent, TestingFolder.Fields.Subfolders);

        Assert.Equal(parent, session.GetSingleAsoValue(child, TestingFolder.Fields.Parent));
    }

    [Fact]
    public void GetTypId_Returns_Empty_For_Missing_Object()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());
        using var session = new DbSession(env);

        Assert.Equal(Guid.Empty, session.GetTypId(Guid.NewGuid()));
    }
}
