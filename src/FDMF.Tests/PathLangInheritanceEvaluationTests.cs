using FDMF.Core;
using FDMF.Core.DatabaseLayer;
using FDMF.Core.PathLayer;
using FDMF.Testing.Shared;
using FDMF.Testing.Shared.InheritanceModelModel;

namespace FDMF.Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public sealed class PathLangInheritanceEvaluationTests
{
    [Fact]
    public void Evaluate_TypeTest_Filters_By_Subtype_And_Allows_Subtype_Field()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetInheritanceModelDumpFile());
        using var session = new DbSession(env);

        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var src = "HasChild(Group): this->Items[$(ChildItem) AND $.ChildValue=42]";
        var parse = PathLangParser.Parse(src);
        Assert.DoesNotContain(parse.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var bind = PathLangBinder.Bind(model, session, parse.Predicates);
        Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var pred = Assert.Single(parse.Predicates);

        var group = new Group(session) { Name = "G" };
        _ = new BaseItem(session) { BaseName = "base", Group = group };

        Assert.False(PathEvaluation.Evaluate(session, group.ObjId, pred, bind.SemanticModel, currentUser: Guid.Empty));

        _ = new ChildItem(session) { BaseName = "child", ChildValue = 41, Group = group };
        Assert.False(PathEvaluation.Evaluate(session, group.ObjId, pred, bind.SemanticModel, currentUser: Guid.Empty));

        _ = new ChildItem(session) { BaseName = "child2", ChildValue = 42, Group = group };
        Assert.True(PathEvaluation.Evaluate(session, group.ObjId, pred, bind.SemanticModel, currentUser: Guid.Empty));
    }
}
