using System.Text.Json;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
using FDMF.Testing.Shared;
using FDMF.Testing.Shared.TestModelModel;

namespace FDMF.Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public sealed class JsonDumpTests
{
    [Fact]
    public void JsonDump_Dumps_Scalar_Fields()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetTestModelDumpFile());

        Guid objId;
        DateTime dt = new DateTime(2001, 02, 03, 04, 05, 06, DateTimeKind.Utc);

        using (var session = new DbSession(env))
        {
            var folder = new TestingFolder(session)
            {
                Name = "Hello"
            };

            folder.TestIntegerField = 42;
            folder.TestDecimalField = 300.45m;
            folder.TestDateField = dt;
            folder.TestBoolField = true;

            objId = folder.ObjId;
            session.Commit();
        }

        using var readSession = new DbSession(env, readOnly: true);
        var json = JsonDump.GetJsonDump(readSession);

        using var doc = JsonDocument.Parse(json);
        var entity = GetEntity(doc, objId);

        Assert.Equal(TestingFolder.TypId.ToString(), entity.GetProperty("$type").GetString());
        Assert.Equal("Hello", entity.GetProperty("Name").GetString());
        Assert.Equal(42L, entity.GetProperty("TestIntegerField").GetInt64());
        Assert.Equal(300.45m, entity.GetProperty("TestDecimalField").GetDecimal());
        Assert.Equal(dt.ToString("O"), entity.GetProperty("TestDateField").GetString());
        Assert.True(entity.GetProperty("TestBoolField").GetBoolean());
    }

    [Fact]
    public void JsonDump_Dumps_Associations_Single_And_Multiple()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetTestModelDumpFile());

        Guid parentId;
        Guid childAId;
        Guid childBId;

        using (var session = new DbSession(env))
        {
            var parent = new TestingFolder(session) { Name = "Parent" };
            var childA = new TestingFolder(session) { Name = "ChildA", Parent = parent };
            var childB = new TestingFolder(session) { Name = "ChildB", Parent = parent };

            parentId = parent.ObjId;
            childAId = childA.ObjId;
            childBId = childB.ObjId;

            session.Commit();
        }

        using var readSession = new DbSession(env, readOnly: true);
        var json = JsonDump.GetJsonDump(readSession);

        using var doc = JsonDocument.Parse(json);

        var parentEntity = GetEntity(doc, parentId);
        var subfolders = parentEntity.GetProperty("Subfolders")
            .EnumerateArray()
            .Select(x => Guid.Parse(x.GetString()!))
            .ToHashSet();

        Assert.Equal(new HashSet<Guid> { childAId, childBId }, subfolders);

        var childAEntity = GetEntity(doc, childAId);
        Assert.Equal(parentId, Guid.Parse(childAEntity.GetProperty("Parent").GetString()!));

        var childBEntity = GetEntity(doc, childBId);
        Assert.Equal(parentId, Guid.Parse(childBEntity.GetProperty("Parent").GetString()!));
    }

    [Fact]
    public void JsonDump_Omits_Unset_Fields_And_Empty_Assocs()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetTestModelDumpFile());

        Guid objId;

        using (var session = new DbSession(env))
        {
            var folder = new TestingFolder(session);
            objId = folder.ObjId;
            session.Commit();
        }

        using var readSession = new DbSession(env, readOnly: true);
        var json = JsonDump.GetJsonDump(readSession);

        using var doc = JsonDocument.Parse(json);
        var entity = GetEntity(doc, objId);

        Assert.Equal(TestingFolder.TypId.ToString(), entity.GetProperty("$type").GetString());
        Assert.False(entity.TryGetProperty("Name", out _));
        Assert.False(entity.TryGetProperty("TestIntegerField", out _));
        Assert.False(entity.TryGetProperty("Parent", out _));
        Assert.False(entity.TryGetProperty("Subfolders", out _));

        // Only "$type" should be present.
        Assert.Single(entity.EnumerateObject());
    }

    private static JsonElement GetEntity(JsonDocument doc, Guid objId)
    {
        var entities = doc.RootElement.GetProperty("entities");
        return entities.GetProperty(objId.ToString());
    }
}
