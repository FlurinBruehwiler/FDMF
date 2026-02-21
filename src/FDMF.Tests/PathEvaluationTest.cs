using FDMF.Core;
using FDMF.Core.DatabaseLayer;
using FDMF.Core.PathLayer;
using FDMF.Testing.Shared;
using FDMF.Testing.Shared.BusinessModelModel;
using FDMF.Testing.Shared.TestModelModel;
using User = FDMF.Testing.Shared.BusinessModelModel.User;

namespace FDMF.Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public sealed class PathEvaluationTest
{
    [Fact]
    public void Basic_Test()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetTestModelDumpFile());
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

        Assert.True(PathEvaluation.Evaluate(session, childFolder.ObjId, predicate, bind.SemanticModel, Guid.Empty));
        Assert.False(PathEvaluation.Evaluate(session, parentFolder.ObjId, predicate, bind.SemanticModel, Guid.Empty));
    }

    [Fact]
    public void Basic_Test_2()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetTestModelDumpFile());
        using var session = new DbSession(env);

        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var parentFolder = new TestingFolder(session);
        parentFolder.Name = "Parent";

        var childFolder = new TestingFolder(session);
        childFolder.Parent = parentFolder;

        var childFolder2 = new TestingFolder(session);
        childFolder2.Parent = parentFolder;

        var src = "P(TestingFolder): this->Parent->Subfolders->Parent->Subfolders->Parent[$.Name=\"Parent\"]";
        var parse = PathLangParser.Parse(src);
        var predicate = parse.Predicates.First();
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);

        Assert.True(PathEvaluation.Evaluate(session, childFolder.ObjId, predicate, bind.SemanticModel, Guid.Empty));
        Assert.True(PathEvaluation.Evaluate(session, childFolder2.ObjId, predicate, bind.SemanticModel, Guid.Empty));
        Assert.False(PathEvaluation.Evaluate(session, parentFolder.ObjId, predicate, bind.SemanticModel, Guid.Empty));
    }

    [Fact]
    public void Basic_Test_3()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetTestModelDumpFile());
        using var session = new DbSession(env);

        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var parentFolder2 = new TestingFolder(session);
        parentFolder2.Name = "Parent2";

        var parentFolder = new TestingFolder(session);
        parentFolder.Name = "Parent";

        var childFolder = new TestingFolder(session);
        childFolder.Parent = parentFolder;

        var childFolder2 = new TestingFolder(session);
        childFolder2.Parent = parentFolder2;

        var src = "P(TestingFolder): this->Parent->Subfolders->Parent->Subfolders->Parent[$.Name=\"Parent\"]";
        var parse = PathLangParser.Parse(src);
        var predicate = parse.Predicates.First();
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);

        Assert.True(PathEvaluation.Evaluate(session, childFolder.ObjId, predicate, bind.SemanticModel, Guid.Empty));
        Assert.False(PathEvaluation.Evaluate(session, childFolder2.ObjId, predicate, bind.SemanticModel, Guid.Empty));
        Assert.False(PathEvaluation.Evaluate(session, parentFolder.ObjId, predicate, bind.SemanticModel, Guid.Empty));
        Assert.False(PathEvaluation.Evaluate(session, parentFolder2.ObjId, predicate, bind.SemanticModel, Guid.Empty));
    }

    [Fact]
    public void RepeatTest()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetTestModelDumpFile());
        using var session = new DbSession(env);

        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var p1 = new TestingFolder(session);

        var p2 = new TestingFolder(session);
        p2.Parent = p1;
        p2.Name = "TargetFolder";

        var p3 = new TestingFolder(session);
        p3.Parent = p2;

        var p4 = new TestingFolder(session);
        p4.Parent = p3;

        var src = "P(TestingFolder): this->repeat(->Parent)[$.Name=\"TargetFolder\"]";
        var parse = PathLangParser.Parse(src);
        var predicate = parse.Predicates.First();
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);

        Assert.True(PathEvaluation.Evaluate(session, p4.ObjId, predicate, bind.SemanticModel, Guid.Empty));
        Assert.False(PathEvaluation.Evaluate(session, p1.ObjId, predicate, bind.SemanticModel, Guid.Empty));
    }

    [Fact]
    public void BusinessModel_Document_To_PublicSession_Via_AgendaItem_With_ComplexFilter()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetBusinessModelDumpFile());
        using var session = new DbSession(env);

        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var publicSession = new Session(session)
        {
            Title = "Budget",
            IsPublic = true,
        };

        var privateSession = new Session(session)
        {
            Title = "Budget",
            IsPublic = false,
        };

        var aiPublic = new AgendaItem(session)
        {
            State = "Ready",
            IsConfidential = false,
            Session = publicSession,
        };

        var aiPrivate = new AgendaItem(session)
        {
            State = "Ready",
            IsConfidential = false,
            Session = privateSession,
        };

        var docA = new Document(session)
        {
            Title = "DocA",
            Locked = false,
        };
        docA.AgendaItems.Add(aiPublic);

        var docB = new Document(session)
        {
            Title = "DocB",
            Locked = false,
        };
        docB.AgendaItems.Add(aiPrivate);

        var src = "P(Document): this->AgendaItems[$.State=\"Ready\" AND $.IsConfidential=false]->Session[$.IsPublic=true AND ($.Title=\"Budget\" OR $.Title=\"Extra\")]";
        var parse = PathLangParser.Parse(src);
        Assert.DoesNotContain(parse.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var predicate = parse.Predicates.First();
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);
        Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        Assert.True(PathEvaluation.Evaluate(session, docA.ObjId, predicate, bind.SemanticModel, Guid.Empty));
        Assert.False(PathEvaluation.Evaluate(session, docB.ObjId, predicate, bind.SemanticModel, Guid.Empty));
    }

    [Fact]
    public void BusinessModel_Document_Can_Reach_Sibling_Document_Through_Folder_Graph()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetBusinessModelDumpFile());
        using var session = new DbSession(env);

        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var root = new Folder(session) { Name = "Root" };
        var finance = new Folder(session) { Name = "Finance", Parent = root };
        var hr = new Folder(session) { Name = "HR", Parent = root };
        var archive = new Folder(session) { Name = "Archive", Parent = root };

        var otherRoot = new Folder(session) { Name = "OtherRoot" };
        var otherChild = new Folder(session) { Name = "Other", Parent = otherRoot };

        var target = new Document(session) { Title = "Target", Locked = false, Folder = hr };
        var start = new Document(session) { Title = "Start", Locked = false, Folder = finance };
        var excludedByFolderFilter = new Document(session) { Title = "Target", Locked = false, Folder = archive };
        var outsideGraph = new Document(session) { Title = "Target", Locked = false, Folder = otherChild };

        var src = "P(Document): this->Folder[$.Name!=\"Archive\"]->Parent[$.Name=\"Root\"]->Subfolders[$.Name!=\"Archive\"]->Documents[$.Title=\"Target\" AND $.Locked=false]";
        var parse = PathLangParser.Parse(src);
        Assert.DoesNotContain(parse.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var predicate = parse.Predicates.First();
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);
        Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        Assert.True(PathEvaluation.Evaluate(session, start.ObjId, predicate, bind.SemanticModel, Guid.Empty));
        Assert.True(PathEvaluation.Evaluate(session, target.ObjId, predicate, bind.SemanticModel, Guid.Empty));
        Assert.False(PathEvaluation.Evaluate(session, excludedByFolderFilter.ObjId, predicate, bind.SemanticModel, Guid.Empty));
        Assert.False(PathEvaluation.Evaluate(session, outsideGraph.ObjId, predicate, bind.SemanticModel, Guid.Empty));
    }

    [Fact]
    public void BusinessModel_Document_To_OwnerUnit_Parent_With_ComplexBusinessCaseFilter()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetBusinessModelDumpFile());
        using var session = new DbSession(env);

        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var ouRoot = new OrganizationalUnit(session) { Code = "OU-ROOT", Name = "Root" };
        var ouDept = new OrganizationalUnit(session) { Code = "OU-DEPT", Name = "Dept", Parent = ouRoot };
        var ouTeam = new OrganizationalUnit(session) { Code = "OU-TEAM", Name = "Team", Parent = ouDept };

        var bcOpen = new BusinessCase(session)
        {
            State = "Open",
            Locked = false,
            OwnerUnit = ouDept,
        };

        var bcClosed = new BusinessCase(session)
        {
            State = "Closed",
            Locked = false,
            OwnerUnit = ouTeam,
        };

        var ok = new Document(session) { Title = "OK", Locked = false, BusinessCase = bcOpen };
        var bad = new Document(session) { Title = "Bad", Locked = false, BusinessCase = bcClosed };

        var src = "P(Document): this->BusinessCase[$.Locked=false AND ($.State=\"Open\" OR $.State=\"InProgress\")]->OwnerUnit->Parent[$.Code=\"OU-ROOT\"]";
        var parse = PathLangParser.Parse(src);
        Assert.DoesNotContain(parse.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var predicate = parse.Predicates.First();
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);
        Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        Assert.True(PathEvaluation.Evaluate(session, ok.ObjId, predicate, bind.SemanticModel, Guid.Empty));
        Assert.False(PathEvaluation.Evaluate(session, bad.ObjId, predicate, bind.SemanticModel, Guid.Empty));
    }

    [Fact]
    public void BusinessModel_Document_To_Category_With_MultiField_Filter()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetBusinessModelDumpFile());
        using var session = new DbSession(env);

        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var confidential = new DocumentCategory(session) { Key = "CONF", Name = "Confidential", IsConfidentialDefault = true };
        var normal = new DocumentCategory(session) { Key = "NORM", Name = "Normal", IsConfidentialDefault = false };

        var docConf = new Document(session) { Title = "C", Locked = false, Category = confidential };
        var docNorm = new Document(session) { Title = "N", Locked = false, Category = normal };

        var src = "P(Document): this->Category[$.IsConfidentialDefault=true AND $.Key=\"CONF\"]";
        var parse = PathLangParser.Parse(src);
        Assert.DoesNotContain(parse.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var predicate = parse.Predicates.First();
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);
        Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        Assert.True(PathEvaluation.Evaluate(session, docConf.ObjId, predicate, bind.SemanticModel, Guid.Empty));
        Assert.False(PathEvaluation.Evaluate(session, docNorm.ObjId, predicate, bind.SemanticModel, Guid.Empty));
    }

    [Fact]
    public void CurrentUser()
    {
        using var env = DbEnvironment.CreateDatabase(dbName: TempDbHelper.GetTempDbDirectory(), dumpFile: TempDbHelper.GetBusinessModelDumpFile());
        using var session = new DbSession(env);
        var model = session.GetObjFromGuid<Model>(env.ModelGuid)!.Value;

        var user = new User(session);

        var document = new Document(session);
        document.CreatedBy = user;

        var otherDocument = new Document(session);

        var src = "P(Document): this->CreatedBy[$.CurrentUser=true]";
        var parse = PathLangParser.Parse(src);
        var bind = PathLangBinder.Bind(model, session, parse.Predicates);
        Assert.Empty(bind.Diagnostics);

        Assert.True(PathEvaluation.Evaluate(session, document.ObjId, parse.Predicates.First(), bind.SemanticModel, user.ObjId));
        Assert.False(PathEvaluation.Evaluate(session, otherDocument.ObjId, parse.Predicates.First(), bind.SemanticModel, user.ObjId));
    }
}
