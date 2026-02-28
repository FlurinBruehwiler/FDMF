using FDMF.Core;
using FDMF.Core.DatabaseLayer;
using FDMF.Testing.Shared;
using FDMF.Testing.Shared.InheritanceModelModel;

namespace FDMF.Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public sealed class SearchInheritanceTests
{
    [Fact]
    public void Search_By_Base_Type_Includes_Derived_Types()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetInheritanceModelDumpFile());

        using (var session = new DbSession(env))
        {
            _ = new BaseItem(session) { BaseName = "b" };
            _ = new ChildItem(session) { BaseName = "c", ChildValue = 1 };
            _ = new GrandChildItem(session) { BaseName = "g", ChildValue = 2, GrandChildFlag = true };
            session.Commit();
        }

        using (var session = new DbSession(env, readOnly: true))
        {
            var results = Searcher.Search<BaseItem>(session);
            Assert.Equal(3, results.Count);
        }
    }

    [Fact]
    public void Search_By_Base_Field_Includes_Derived_Types()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetInheritanceModelDumpFile());

        Guid childId;
        using (var session = new DbSession(env))
        {
            _ = new BaseItem(session) { BaseName = "match" };
            var child = new ChildItem(session) { BaseName = "match", ChildValue = 7 };
            childId = child.ObjId;
            session.Commit();
        }

        using (var session = new DbSession(env, readOnly: true))
        {
            var results = Searcher.Search<BaseItem>(session, new StringCriterion
            {
                FieldId = BaseItem.Fields.BaseName,
                Type = StringCriterion.MatchType.Exact,
                Value = "match"
            });

            Assert.Equal(2, results.Count);
            Assert.Contains(results, x => x.ObjId == childId);
        }
    }
}
