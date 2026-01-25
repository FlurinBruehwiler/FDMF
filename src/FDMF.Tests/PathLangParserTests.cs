using FDMF.Core.PathLayer;

namespace FDMF.Tests;

public sealed class PathLangParserTests
{
    [Fact]
    public void ParsePredicate_SimpleTraversalWithFieldGuard()
    {
        var src = "OwnerCanView(Document): this->Business->Owners[$(Person).CurrentUser=true]";
        var p = new PathLangParser(src);
        var result = p.ParsePredicateDef();
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
        var pred = Assert.Single(result.Predicates);

        Assert.Equal("OwnerCanView", pred.Name.Text.ToString());
        Assert.Equal("Document", pred.InputType.Text.ToString());

        // this->Business->Owners[...]
        var t2 = Assert.IsType<AstTraverseExpr>(pred.Body);
        Assert.Equal("Owners", t2.AssocName.Text.ToString());

        var t1 = Assert.IsType<AstTraverseExpr>(t2.Source);
        Assert.Equal("Business", t1.AssocName.Text.ToString());
        Assert.IsType<AstThisExpr>(t1.Source);

        var filter = t2.Filter;
        Assert.NotNull(filter);
        var cond = Assert.IsType<AstFieldCompareCondition>(filter!.Condition);
        Assert.Equal("Person", cond.TypeGuard!.Value.Text.ToString());
        Assert.Equal("CurrentUser", cond.FieldName.Text.ToString());
        Assert.Equal(AstCompareOp.Equals, cond.Op);
        Assert.True(Assert.IsType<AstBoolLiteral>(cond.Value).Value);
    }

    [Fact]
    public void ParsePredicate_LogicalAndOr_And_PredicateCallCompare()
    {
        var src = "CanEdit(Document): (Viewable(this)=true AND Editable(this)=true) OR OwnerCanView(this)=true";
        var p = new PathLangParser(src);
        var result = p.ParsePredicateDef();
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
        var pred = Assert.Single(result.Predicates);

        var or = Assert.IsType<AstLogicalExpr>(pred.Body);
        Assert.Equal(AstLogicalOp.Or, or.Op);

        var and = Assert.IsType<AstLogicalExpr>(or.Left);
        Assert.Equal(AstLogicalOp.And, and.Op);

        AssertCall(and.Left, "Viewable");
        AssertCall(and.Right, "Editable");
        AssertCall(or.Right, "OwnerCanView");
    }

    [Fact]
    public void ParsePredicate_FilterPredicateCall()
    {
        var src = "TaskViewable(Task): this->Document[Viewable($)=true]";
        var p = new PathLangParser(src);
        var result = p.ParsePredicateDef();
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
        var pred = Assert.Single(result.Predicates);

        var trav = Assert.IsType<AstTraverseExpr>(pred.Body);
        Assert.Equal("Document", trav.AssocName.Text.ToString());
        Assert.IsType<AstThisExpr>(trav.Source);

        var cond = Assert.IsType<AstPredicateCompareCondition>(trav.Filter!.Condition);
        Assert.Equal("Viewable", cond.PredicateName.Text.ToString());
        Assert.IsType<AstCurrentExpr>(cond.Argument);
        Assert.Equal(AstCompareOp.Equals, cond.Op);
        Assert.True(Assert.IsType<AstBoolLiteral>(cond.Value).Value);
    }

    private static void AssertCall(AstExpr expr, string name)
    {
        var call = Assert.IsType<AstPredicateCallExpr>(expr);
        Assert.Equal(name, call.PredicateName.Text.ToString());
        Assert.IsType<AstThisExpr>(call.Argument);
    }

    [Fact]
    public void ParsePredicate_MissingRParen_ReportsError_DoesNotThrow()
    {
        var src = "P(Document: this->A";
        var ex = Record.Exception(() => new PathLangParser(src).ParsePredicateDef());
        Assert.Null(ex);

        var result = new PathLangParser(src).ParsePredicateDef();
        Assert.Contains(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
        Assert.Single(result.Predicates);
        Assert.Equal("P", result.Predicates[0].Name.Text.ToString());
    }

    [Fact]
    public void ParsePredicate_BadArgExpr_ReportsError_Continues()
    {
        var src = "P(Document): Visible(123)=true";
        var result = new PathLangParser(src).ParsePredicateDef();
        Assert.Contains(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var pred = Assert.Single(result.Predicates);
        var call = Assert.IsType<AstPredicateCallExpr>(pred.Body);
        Assert.Equal("Visible", call.PredicateName.Text.ToString());
        Assert.IsType<AstErrorExpr>(call.Argument);
    }

    [Fact]
    public void ParseProgram_RecoversAndParsesFollowingPredicate()
    {
        var src = "Bad(Document): this->A[$.X=]\nGood(Document): this[$.Ok=true]";
        var result = new PathLangParser(src).ParseProgram();

        Assert.Contains(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
        Assert.Equal(2, result.Predicates.Count);
        Assert.Equal("Bad", result.Predicates[0].Name.Text.ToString());
        Assert.Equal("Good", result.Predicates[1].Name.Text.ToString());
    }

    [Fact]
    public void ParsePredicate_UnclosedFilterBracket_ReportsError_DoesNotThrow()
    {
        var src = "P(Document): this[$.Ok=true";
        var ex = Record.Exception(() => new PathLangParser(src).ParsePredicateDef());
        Assert.Null(ex);

        var result = new PathLangParser(src).ParsePredicateDef();
        Assert.Contains(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
    }
}
