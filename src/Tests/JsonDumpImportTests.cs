using System.Text.Json;
using Model.Generated;
using Shared;
using Shared.Database;
using TestModel.Generated;
using Environment = Shared.Environment;

namespace Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public class JsonDumpImportTests
{
    [Fact]
    public void FromJson_Creates_Objects_And_Fields_And_Assocs()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory());

        using (var session = new DbSession(env))
        {
            var json = File.ReadAllText("testdata/TestModelDump.json");
            JsonDump.FromJson(json, session);

            session.Commit();
        }

        using var readSession = new DbSession(env, readOnly: true);

        var obj = readSession.GetObjFromGuid<EntityDefinition>(Guid.Parse("e5184bba-f470-4bab-aeed-28fb907da349"));

        Assert.NotNull(obj);
        Assert.Equal("TestingFolder", obj.Value.Name);
    }

    [Fact]
    public void FromJson_Updates_Existing_Object_Instead_Of_Creating_Duplicate()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());

        Guid fixedId;

        using (var session = new DbSession(env))
        {
            var folder = new TestingFolder(session) { Name = "Before" };
            fixedId = folder.ObjId;
            session.Commit();
        }

        using (var session = new DbSession(env))
        {
            var payload = new
            {
                modelGuid = Guid.NewGuid().ToString(),
                entities = new Dictionary<string, object>
                {
                    [fixedId.ToString()] = new Dictionary<string, object>
                    {
                        ["$type"] = TestingFolder.TypId.ToString(),
                        ["Name"] = "After",
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);


            JsonDump.FromJson(json, session);
            session.Commit();
        }

        using var readSession = new DbSession(env, readOnly: true);
        var loaded = readSession.GetObjFromGuid<TestingFolder>(fixedId)!.Value;
        Assert.Equal("After", loaded.Name);

        var count = Searcher.Search<TestingFolder>(readSession).Count();
        Assert.Equal(1, count);
    }

    [Fact]
    public void FromJson_Removes_Missing_Fields_And_Assocs_To_Match_Json()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());

        Guid aId;
        Guid bId;

        using (var session = new DbSession(env))
        {
            var a = new TestingFolder(session) { Name = "A" };
            var b = new TestingFolder(session) { Name = "B" };
            a.Parent = b;
            a.TestIntegerField = 123;

            aId = a.ObjId;
            bId = b.ObjId;
            session.Commit();
        }

        using (var session = new DbSession(env))
        {
            var payload = new
            {
                modelGuid = Guid.NewGuid().ToString(),
                entities = new Dictionary<string, object>
                {
                    [aId.ToString()] = new Dictionary<string, object>
                    {
                        ["$type"] = TestingFolder.TypId.ToString(),
                        ["Name"] = "A",
                    },
                    [bId.ToString()] = new Dictionary<string, object>
                    {
                        ["$type"] = TestingFolder.TypId.ToString(),
                        ["Name"] = "B",
                    },
                }
            };

            var json = JsonSerializer.Serialize(payload);


            JsonDump.FromJson(json, session);
            session.Commit();
        }

        using var readSession = new DbSession(env, readOnly: true);
        var aReloaded = readSession.GetObjFromGuid<TestingFolder>(aId)!.Value;

        Assert.Equal("A", aReloaded.Name);

        // Unset numeric fields are stored as "missing" (no VAL entry), which the generated getter can't read.
        Assert.Empty(readSession.GetFldValue(aId, TestingFolder.Fields.TestIntegerField));

        Assert.Null(aReloaded.Parent);
    }
}
