using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BaseModel.Generated;
using FDMF.Core.Database;
using FDMF.Core.PathLayer;
using Environment = FDMF.Core.Environment;

namespace FDMF.Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public sealed class PathLangBinderBusinessModelTests
{
    private static Model LoadBusinessModel(Environment env, DbSession session)
    {
        var model = session.GetObjFromGuid<Model>(env.ModelGuid);
        Assert.NotNull(model);
        return model!.Value;
    }

    private static IEnumerable<AstTraverseExpr> EnumerateTraverses(AstExpr expr)
    {
        switch (expr)
        {
            case AstTraverseExpr t:
                yield return t;
                foreach (var x in EnumerateTraverses(t.Source))
                    yield return x;
                break;
            case AstFilterExpr f:
                foreach (var x in EnumerateTraverses(f.Source))
                    yield return x;
                break;
            case AstRepeatExpr r:
                foreach (var x in EnumerateTraverses(r.Expr))
                    yield return x;
                break;
            case AstLogicalExpr l:
                foreach (var x in EnumerateTraverses(l.Left))
                    yield return x;
                foreach (var x in EnumerateTraverses(l.Right))
                    yield return x;
                break;
            default:
                yield break;
        }
    }

    private static IEnumerable<AstFieldCompareCondition> EnumerateFieldCompares(AstExpr expr)
    {
        foreach (var t in EnumerateTraverses(expr))
        {
            if (t.Filter?.Condition is AstFieldCompareCondition fc)
                yield return fc;
        }

        if (expr is AstFilterExpr fe && fe.Filter.Condition is AstFieldCompareCondition fc2)
            yield return fc2;
    }

    [Fact]
    public void Bind_ComplexPredicate_ResolvesNestedTraversals_AndFields()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetBusinessModelDumpFile());
        using var session = new DbSession(env, readOnly: true);
        var model = LoadBusinessModel(env, session);

        var src =
            "CanEdit(Document): this->BusinessCase[$.Locked=false] AND (this->OwnerUnit->Members[$(User).CurrentUser=true] OR this->ExplicitViewers[$.CurrentUser=true])";

        var parse = new PathLangParser(src).ParsePredicateDef();
        Assert.DoesNotContain(parse.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var bind = PathLangBinder.Bind(model, parse.Predicates);
        Assert.DoesNotContain(bind.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var pred = Assert.Single(parse.Predicates);
        Assert.Equal(Guid.Parse("40d51f0a-31f0-4a04-9d16-77f454fd914e"), bind.SemanticModel.InputTypIdByPredicate[pred]);

        // Assoc ids we expect to be resolved.
        var doc_BusinessCase = Guid.Parse("2df0f6aa-582d-4daf-89a1-def70bf75aed");
        var doc_OwnerUnit = Guid.Parse("35cfa966-9d9f-434f-95a7-507bb313f26f");
        var ou_Members = Guid.Parse("f86ce70e-57df-4056-8ff2-9cc0f6af444e");
        var doc_ExplicitViewers = Guid.Parse("3f0771ed-7629-495c-bff2-5b8d3939f169");

        var traverses = EnumerateTraverses(pred.Body).ToList();
        Assert.Contains(traverses, t => t.AssocName.Text.ToString() == "BusinessCase" && bind.SemanticModel.AssocByTraverse[t].Values.Any(v => v.AssocFldId == doc_BusinessCase));
        Assert.Contains(traverses, t => t.AssocName.Text.ToString() == "OwnerUnit" && bind.SemanticModel.AssocByTraverse[t].Values.Any(v => v.AssocFldId == doc_OwnerUnit));
        Assert.Contains(traverses, t => t.AssocName.Text.ToString() == "Members" && bind.SemanticModel.AssocByTraverse[t].Values.Any(v => v.AssocFldId == ou_Members));
        Assert.Contains(traverses, t => t.AssocName.Text.ToString() == "ExplicitViewers" && bind.SemanticModel.AssocByTraverse[t].Values.Any(v => v.AssocFldId == doc_ExplicitViewers));

        // Field ids we expect to be resolved.
        var bc_Locked = Guid.Parse("0c52f663-a159-4e40-91e4-f7327b13e793");
        var user_CurrentUser = Guid.Parse("9b76cfbf-e9b5-41db-b655-b496fca80732");
        var user_TypId = Guid.Parse("3777d451-b036-4772-9358-5a67ab44763b");

        var fieldCompares = EnumerateFieldCompares(pred.Body).ToList();
        Assert.Contains(fieldCompares, fc => fc.FieldName.Text.ToString() == "Locked" && bind.SemanticModel.FieldByCompare[fc].Values.Any(v => v.FldId == bc_Locked));
        Assert.Contains(fieldCompares, fc => fc.FieldName.Text.ToString() == "CurrentUser" && bind.SemanticModel.FieldByCompare[fc].Values.Any(v => v.FldId == user_CurrentUser));

        // Ensure the type-guard was resolved for $(User).CurrentUser.
        var guarded = fieldCompares.Single(fc => fc.TypeGuard is not null);
        Assert.Equal(user_TypId, bind.SemanticModel.TypeGuardTypIdByCompare[guarded]);
    }

    [Fact]
    public void Bind_UnknownAssociation_ReturnsHelpfulError()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetBusinessModelDumpFile());
        using var session = new DbSession(env, readOnly: true);
        var model = LoadBusinessModel(env, session);

        var src = "Bad(Document): this->DoesNotExist->Members[$.CurrentUser=true]";
        var parse = new PathLangParser(src).ParsePredicateDef();
        var bind = PathLangBinder.Bind(model, parse.Predicates);

        Assert.Contains(bind.Diagnostics, d =>
            d.Severity == PathLangDiagnosticSeverity.Error &&
            d.Message == "Unknown association 'DoesNotExist' on type 'Document'");
    }

    [Fact]
    public void Bind_UnknownField_ReturnsHelpfulError()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetBusinessModelDumpFile());
        using var session = new DbSession(env, readOnly: true);
        var model = LoadBusinessModel(env, session);

        var src = "Bad(Document): this->OwnerUnit[$.DoesNotExist=true]";
        var parse = new PathLangParser(src).ParsePredicateDef();
        var bind = PathLangBinder.Bind(model, parse.Predicates);

        Assert.Contains(bind.Diagnostics, d =>
            d.Severity == PathLangDiagnosticSeverity.Error &&
            d.Message == "Unknown field 'DoesNotExist' on type 'OrganizationalUnit'");
    }

    [Fact]
    public void Bind_UnknownType_ReturnsHelpfulError()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetBusinessModelDumpFile());
        using var session = new DbSession(env, readOnly: true);
        var model = LoadBusinessModel(env, session);

        var src = "Bad(DoesNotExist): this";
        var parse = new PathLangParser(src).ParsePredicateDef();
        var bind = PathLangBinder.Bind(model, parse.Predicates);

        Assert.Contains(bind.Diagnostics, d =>
            d.Severity == PathLangDiagnosticSeverity.Error &&
            d.Message == "Unknown type 'DoesNotExist'");
    }
}
