using System.Text;
using FDMF.Core;
using FDMF.Core.DatabaseLayer;
using FDMF.Core.PathLayer;
using FDMF.Testing.Shared;
using FDMF.Testing.Shared.BusinessModelModel;

namespace FDMF.Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public sealed class PathLangBusinessModelInheritanceEvaluationTests
{
    [Fact]
    public void Evaluate_TypeTest_Allows_Subtype_Field_After_Base_Assoc()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetBusinessModelDumpFile());
        using var session = new DbSession(env);

        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var src = "HasSessionCat(Document): this->Category[$(Session) AND $.Title=\"S1\"]";
        var parse = PathLangParser.Parse(src);
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);
        Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var pred = Assert.Single(parse.Predicates);

        var doc = new Document(session) { Title = "D1" };
        var sess = new Session(session) { Title = "S1" };

        // Wire doc.Category -> sess (Category is typed as DocumentCategory).
        session.CreateAso(doc.ObjId, Document.Fields.Category, sess.ObjId, DocumentCategory.Fields.Documents);

        Assert.True(PathEvaluation.Evaluate(session, doc.ObjId, pred, bind.SemanticModel, currentUser: Guid.Empty));

        var doc2 = new Document(session) { Title = "D2" };
        var cat = new DocumentCategory(session) { Name = "C" };
        session.CreateAso(doc2.ObjId, Document.Fields.Category, cat.ObjId, DocumentCategory.Fields.Documents);

        Assert.False(PathEvaluation.Evaluate(session, doc2.ObjId, pred, bind.SemanticModel, currentUser: Guid.Empty));
    }

    [Fact]
    public void Evaluate_TypeGuard_FieldCompare_Acts_As_TypeCheck()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetBusinessModelDumpFile());
        using var session = new DbSession(env);
        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var src = "HasSessionCat(Document): this->Category[$(Session).Title=\"S1\"]";
        var parse = PathLangParser.Parse(src);
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);
        Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
        var pred = Assert.Single(parse.Predicates);

        var doc = new Document(session) { Title = "D" };
        var sess = new Session(session) { Title = "S1" };
        session.CreateAso(doc.ObjId, Document.Fields.Category, sess.ObjId, DocumentCategory.Fields.Documents);
        Assert.True(PathEvaluation.Evaluate(session, doc.ObjId, pred, bind.SemanticModel, currentUser: Guid.Empty));

        var doc2 = new Document(session) { Title = "D2" };
        var cat = new DocumentCategory(session) { Name = "C" };
        session.CreateAso(doc2.ObjId, Document.Fields.Category, cat.ObjId, DocumentCategory.Fields.Documents);
        Assert.False(PathEvaluation.Evaluate(session, doc2.ObjId, pred, bind.SemanticModel, currentUser: Guid.Empty));
    }

    [Fact]
    public void Evaluate_Inherited_Field_Access_Works_After_Narrowing()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetBusinessModelDumpFile());
        using var session = new DbSession(env);
        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var src = "HasSessionKey(Document): this->Category[$(Session) AND $.Key=\"K1\"]";
        var parse = PathLangParser.Parse(src);
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);
        Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
        var pred = Assert.Single(parse.Predicates);

        var doc = new Document(session) { Title = "D" };
        var sess = new Session(session) { Title = "S" };

        // Set inherited DocumentCategory.Key on sess.
        session.SetFldValue(sess.ObjId, DocumentCategory.Fields.Key, Encoding.Unicode.GetBytes("K1"));
        session.CreateAso(doc.ObjId, Document.Fields.Category, sess.ObjId, DocumentCategory.Fields.Documents);

        Assert.True(PathEvaluation.Evaluate(session, doc.ObjId, pred, bind.SemanticModel, currentUser: Guid.Empty));
    }

    [Fact]
    public void Evaluate_BaseTyped_Predicate_Accepts_Derived_Object_And_Inherited_Assoc()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetBusinessModelDumpFile());
        using var session = new DbSession(env);
        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var src = "HasDocs(DocumentCategory): this->Documents[$.Title=\"ChildDoc\"]";
        var parse = PathLangParser.Parse(src);
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);
        Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
        var pred = Assert.Single(parse.Predicates);

        // thisObj is a Session, but predicate expects DocumentCategory.
        var sess = new Session(session) { Title = "S" };
        var doc = new Document(session) { Title = "ChildDoc" };
        session.CreateAso(doc.ObjId, Document.Fields.Category, sess.ObjId, DocumentCategory.Fields.Documents);

        Assert.True(PathEvaluation.Evaluate(session, sess.ObjId, pred, bind.SemanticModel, currentUser: Guid.Empty));
    }
}
