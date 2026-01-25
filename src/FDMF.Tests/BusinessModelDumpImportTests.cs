using BaseModel.Generated;
using Environment = FDMF.Core.Environment;
using FDMF.Core.Database;

namespace FDMF.Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public sealed class BusinessModelDumpImportTests
{
    [Fact]
    public void Create_Db_From_BusinessModel_Dump()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetBusinessModelDumpFile());
        using var session = new DbSession(env, readOnly: true);

        var model = session.GetObjFromGuid<Model>(env.ModelGuid);
        Assert.NotNull(model);
        Assert.Equal("BusinessModel", model!.Value.Name);

        var orgUnitId = Guid.Parse("81daacd7-4b3f-44bf-b73c-52c7012ad758");
        var ou = session.GetObjFromGuid<EntityDefinition>(orgUnitId);
        Assert.NotNull(ou);
        Assert.Equal("OrganizationalUnit", ou!.Value.Key);

        var ouParentRefId = Guid.Parse("77e01c0c-f7a4-47e8-b996-3790d2f601f6");
        var ouChildrenRefId = Guid.Parse("c1e8b920-cd17-40b1-8785-ea2d9ca84b59");

        var parentRef = session.GetObjFromGuid<ReferenceFieldDefinition>(ouParentRefId);
        var childRef = session.GetObjFromGuid<ReferenceFieldDefinition>(ouChildrenRefId);

        Assert.NotNull(parentRef);
        Assert.NotNull(childRef);
        Assert.Equal("Parent", parentRef!.Value.Key);
        Assert.Equal("Children", childRef!.Value.Key);
        Assert.Equal(ouChildrenRefId, parentRef.Value.OtherReferenceFields.ObjId);
        Assert.Equal(ouParentRefId, childRef.Value.OtherReferenceFields.ObjId);
    }
}
