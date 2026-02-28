using FDMF.Core;
using FDMF.Core.DatabaseLayer;
using FDMF.Testing.Shared;
using FDMF.Testing.Shared.InheritanceModelModel;

namespace FDMF.Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public sealed class InheritanceModelTests
{
    [Fact]
    public void Import_Creates_Inheritance_Graph()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetInheritanceModelDumpFile());
        using var session = new DbSession(env, readOnly: true);

        var baseEd = session.GetObjFromGuid<EntityDefinition>(BaseItem.TypId)!.Value;
        var childEd = session.GetObjFromGuid<EntityDefinition>(ChildItem.TypId)!.Value;
        var grandChildEd = session.GetObjFromGuid<EntityDefinition>(GrandChildItem.TypId)!.Value;

        Assert.Equal("BaseItem", childEd.Parent!.Value.Key);
        Assert.Equal("ChildItem", grandChildEd.Parent!.Value.Key);

        Assert.Contains(baseEd.Children, x => x.Key == "ChildItem");
        Assert.Contains(childEd.Children, x => x.Key == "GrandChildItem");
    }

    [Fact]
    public void Child_Sees_Inherited_Fields_And_Assocs()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetInheritanceModelDumpFile());
        using var session = new DbSession(env);

        var group = new Group(session) { Name = "G1" };

        var child = new ChildItem(session)
        {
            BaseName = "base",
            ChildValue = 42,
            Group = group,
        };

        Assert.Equal("base", child.BaseName);
        Assert.Equal(42, child.ChildValue);
        Assert.Equal(group.ObjId, child.Group!.Value.ObjId);

        Assert.Contains(group.Items, x => x.ObjId == child.ObjId);
    }

    [Fact]
    public void Casting_Operators_Work()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetInheritanceModelDumpFile());
        using var session = new DbSession(env);

        var group = new Group(session) { Name = "G1" };

        var child = new ChildItem(session) { BaseName = "base", ChildValue = 1, Group = group };
        var grandChild = new GrandChildItem(session) { BaseName = "base2", ChildValue = 2, GrandChildFlag = true, Group = group };

        BaseItem asBase = child;
        Assert.Equal(child.ObjId, asBase.ObjId);
        Assert.Equal("base", asBase.BaseName);

        var backToChild = (ChildItem)asBase;
        Assert.Equal(child.ObjId, backToChild.ObjId);

        Assert.True(ChildItem.TryCastFrom(asBase, out var tryChild));
        Assert.Equal(child.ObjId, tryChild.ObjId);

        BaseItem baseFromGrand = grandChild;
        var backToGrand = (GrandChildItem)baseFromGrand;
        Assert.Equal(grandChild.ObjId, backToGrand.ObjId);

        Assert.Throws<InvalidCastException>(() => (GrandChildItem)asBase);
    }

    [Fact]
    public void DbSession_BaseType_Read_Allows_Derived()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetInheritanceModelDumpFile());
        using var session = new DbSession(env);

        var child = new ChildItem(session) { BaseName = "b", ChildValue = 7 };

        var baseRead = session.GetObjFromGuid<BaseItem>(child.ObjId);
        Assert.NotNull(baseRead);
        Assert.Equal("b", baseRead!.Value.BaseName);

        var down = (ChildItem)baseRead.Value;
        Assert.Equal(child.ObjId, down.ObjId);

        var baseOnly = new BaseItem(session) { BaseName = "x" };
        Assert.False(ChildItem.TryCastFrom(baseOnly, out _));
        Assert.Throws<InvalidCastException>(() => (ChildItem)baseOnly);
    }
}
