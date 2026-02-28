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

        var src = "HasPdf(Folder): this->Documents[$(PdfDocument) AND $.PageCount=10]";
        var parse = PathLangParser.Parse(src);
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);
        Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var pred = Assert.Single(parse.Predicates);

        var folder = new Folder(session) { Name = "F", Path = "/", CreatedAt = DateTime.UtcNow };
        _ = new Document(session) { Title = "Doc", Folder = folder };
        _ = new PdfDocument(session) { Title = "Pdf", Folder = folder, PageCount = 9 };

        Assert.False(PathEvaluation.Evaluate(session, folder.ObjId, pred, bind.SemanticModel, currentUser: Guid.Empty));

        _ = new PdfDocument(session) { Title = "Pdf2", Folder = folder, PageCount = 10 };
        Assert.True(PathEvaluation.Evaluate(session, folder.ObjId, pred, bind.SemanticModel, currentUser: Guid.Empty));
    }

    [Fact]
    public void Evaluate_TypeGuard_FieldCompare_Acts_As_TypeCheck()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetBusinessModelDumpFile());
        using var session = new DbSession(env);
        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var src = "HasPdf(Folder): this->Documents[$(PdfDocument).PageCount=10]";
        var parse = PathLangParser.Parse(src);
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);
        Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
        var pred = Assert.Single(parse.Predicates);

        var folder = new Folder(session) { Name = "F", Path = "/", CreatedAt = DateTime.UtcNow };
        _ = new Document(session) { Title = "Doc", Folder = folder };
        _ = new PdfDocument(session) { Title = "Pdf", Folder = folder, PageCount = 10 };

        Assert.True(PathEvaluation.Evaluate(session, folder.ObjId, pred, bind.SemanticModel, currentUser: Guid.Empty));
    }

    [Fact]
    public void Evaluate_Inherited_Field_Access_Works_After_Narrowing()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetBusinessModelDumpFile());
        using var session = new DbSession(env);
        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var src = "HasPdfTitle(Folder): this->Documents[$(PdfDocument) AND $.Title=\"T1\"]";
        var parse = PathLangParser.Parse(src);
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);
        Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
        var pred = Assert.Single(parse.Predicates);

        var folder = new Folder(session) { Name = "F", Path = "/", CreatedAt = DateTime.UtcNow };
        _ = new PdfDocument(session) { Title = "T1", Folder = folder, PageCount = 1 };
        Assert.True(PathEvaluation.Evaluate(session, folder.ObjId, pred, bind.SemanticModel, currentUser: Guid.Empty));
    }

    [Fact]
    public void Evaluate_BaseTyped_Predicate_Accepts_Derived_Object_And_Inherited_Assoc()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetBusinessModelDumpFile());
        using var session = new DbSession(env);
        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var src = "HasFolder(Document): this->Folder[$.Name=\"F1\"]";
        var parse = PathLangParser.Parse(src);
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);
        Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
        var pred = Assert.Single(parse.Predicates);

        var folder = new Folder(session) { Name = "F1", Path = "/", CreatedAt = DateTime.UtcNow };
        var pdf = new PdfDocument(session) { Title = "Pdf", Folder = folder, PageCount = 1 };

        Assert.True(PathEvaluation.Evaluate(session, pdf.ObjId, pred, bind.SemanticModel, currentUser: Guid.Empty));
    }
}
