using FDMF.Core;
using FDMF.Core.DatabaseLayer;
using FDMF.Testing.Shared;

namespace FDMF.Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public sealed class JsonDumpImportTests
{
    [Fact]
    public void Create_EmptyDb_And_Import_From_Dump()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory());

        using var session = new DbSession(env);
        {
            var json = File.ReadAllText("testdata/TestModelDump.json");
            var modelGuid = JsonDump.FromJson(json, session);
            //session.Commit();
            env.ModelGuid = modelGuid;
        }

        //using var readSession = new DbSession(env, readOnly: true);

        var model = session.GetObjFromGuid<Model>(env.ModelGuid);
        Assert.NotNull(model);
        Assert.Equal("TestModel", model.Value.Name);
        
        var testingDocument = session.GetObjFromGuid<EntityDefinition>(Guid.Parse("e5184bba-f470-4bab-aeed-28fb907da349"));
        Assert.NotNull(testingDocument);
        Assert.Equal("TestingDocument", testingDocument.Value.Name);
        Assert.Equal("TestingDocument", testingDocument.Value.Key);
        Assert.Equal("e5184bba-f470-4bab-aeed-28fb907da349", testingDocument.Value.Id);
        Assert.Equal(model, testingDocument.Value.Model);
        
        var testingFolder = session.GetObjFromGuid<EntityDefinition>(Guid.Parse("a3ccbd8b-2256-414b-a402-1a091cb407a5"));
        Assert.NotNull(testingFolder);
        Assert.Equal("TestingFolder", testingFolder.Value.Name);
        Assert.Equal("a3ccbd8b-2256-414b-a402-1a091cb407a5", testingFolder.Value.Id);
        Assert.Equal(model, testingFolder.Value.Model);
        Assert.Equal(5, testingFolder.Value.FieldDefinitions.Count);
        Assert.Equal(2, testingFolder.Value.ReferenceFieldDefinitions.Count);
        Assert.Equal("TestingFolder", testingFolder.Value.Key);
    }

    [Fact]
    public void Create_Db_From_Dump()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetTestModelDumpFile());

        using var readSession = new DbSession(env, readOnly: true);

        var obj = readSession.GetObjFromGuid<EntityDefinition>(Guid.Parse("a3ccbd8b-2256-414b-a402-1a091cb407a5"));

        Assert.NotNull(obj);
        Assert.Equal("TestingFolder", obj.Value.Name);
    }
}
