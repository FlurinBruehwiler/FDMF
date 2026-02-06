using BaseModel.Generated;
using FDMF.Core.Database;
using FDMF.Core.PathLayer;
using TestModel.Generated;
using Environment = FDMF.Core.Environment;

namespace FDMF.Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public sealed class PathEvaluationTest
{
    [Fact]
    public void Test()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());
        using var session = new DbSession(env);

        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var childFolder = new TestingFolder(session);
        var parentFolder = new TestingFolder(session);
        parentFolder.Name = "Parent";
        childFolder.Parent = parentFolder;

        var src = "P(TestingFolder): this->Parent[$.Name=\"Parent\"]";
        var parse = PathLangParser.Parse(src);
        var predicate = parse.Predicates.First();
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);

        Assert.True(PathEvaluation.Evaluate(session, childFolder.ObjId, predicate, bind.SemanticModel));
        Assert.False(PathEvaluation.Evaluate(session, parentFolder.ObjId, predicate, bind.SemanticModel));
    }
}