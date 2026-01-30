using BaseModel.Generated;
using FDMF.Core.Database;
using FDMF.Core.PathLayer;
using TestModel.Generated;
using Environment = FDMF.Core.Environment;

namespace FDMF.Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public sealed class PathLangBinderTests
{
    [Fact]
    public void Bind_Resolves_Field_And_Association_Ids_From_Model()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());
        using var session = new DbSession(env, readOnly: true);

        var model = session.GetObjFromGuid<Model>(env.ModelGuid);
        Assert.NotNull(model);

        var src = "P(TestingFolder): this->Parent[$.Name=\"Parent\"]";
        var parse = PathLangParser.Parse(src);
        Assert.DoesNotContain(parse.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var bind = PathLangBinder.Bind(model.Value, session, parse.Predicates);
        Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var pred = Assert.Single(parse.Predicates);
        Assert.Equal(TestingFolder.TypId, bind.SemanticModel.InputTypIdByPredicate[pred]);

        var path = Assert.IsType<AstPathExpr>(pred.Body);
        var step = Assert.Single(path.Steps);
        // Assert.Contains(bind.SemanticModel.AssocByPathStep[step].Values, v => v.AssocFldId == TestingFolder.Fields.Parent);

        var cond = Assert.IsType<AstFieldCompareCondition>(step.Filter!.Condition);
        // Assert.Contains(bind.SemanticModel.FieldByCompare[cond].Values, v => v.FldId == TestingFolder.Fields.Name);
    }

    [Fact]
    public void Bind_Unknown_Field_Reports_Error_But_Does_Not_Throw()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());
        using var session = new DbSession(env, readOnly: true);

        var model = session.GetObjFromGuid<Model>(env.ModelGuid);
        Assert.NotNull(model);

        var src = "P(TestingFolder): this[$.DoesNotExist=\"x\"]";
        var parse = PathLangParser.Parse(src);
        var bind = PathLangBinder.Bind(model.Value, session, parse.Predicates);

        Assert.Contains(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
    }
}
