using FDMF.Core.PathLayer;

namespace FDMF.Tests;

public sealed class PathLangParserTests
{
    [Fact]
    public void ParsePredicate_SimpleTraversalWithFieldGuard()
    {
        var src = "OwnerCanView(Document): this->Business->Owners[$(Person).CurrentUser=true]";
        var result = PathLangParser.Parse(src);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
        var pred = Assert.Single(result.Predicates);

        Assert.Equal("OwnerCanView", pred.Name.Text.ToString());
        Assert.Equal("Document", pred.InputType.Text.ToString());

        // this->Business->Owners[...]
        var path = Assert.IsType<AstPathExpr>(pred.Body);
        Assert.IsType<AstThisExpr>(path.Source);
        Assert.Equal(2, path.Steps.Count);
        Assert.Equal("Business", path.Steps[0].AssocName.Text.ToString());
        Assert.Null(path.Steps[0].Filter);
        Assert.Equal("Owners", path.Steps[1].AssocName.Text.ToString());

        var filter = path.Steps[1].Filter;
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
        var result = PathLangParser.Parse(src);
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
        var result = PathLangParser.Parse(src);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
        var pred = Assert.Single(result.Predicates);

        var path = Assert.IsType<AstPathExpr>(pred.Body);
        Assert.IsType<AstThisExpr>(path.Source);
        var step = Assert.Single(path.Steps);
        Assert.Equal("Document", step.AssocName.Text.ToString());

        var cond = Assert.IsType<AstPredicateCompareCondition>(step.Filter!.Condition);
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
        var ex = Record.Exception(() => PathLangParser.Parse(src));
        Assert.Null(ex);

        var result = PathLangParser.Parse(src);
        Assert.Contains(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
        Assert.Single(result.Predicates);
        Assert.Equal("P", result.Predicates[0].Name.Text.ToString());
    }

    [Fact]
    public void ParsePredicate_BadArgExpr_ReportsError_Continues()
    {
        var src = "P(Document): Visible(123)=true";
        var result = PathLangParser.Parse(src);
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
        var result = PathLangParser.Parse(src);

        Assert.Contains(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
        Assert.Equal(2, result.Predicates.Count);
        Assert.Equal("Bad", result.Predicates[0].Name.Text.ToString());
        Assert.Equal("Good", result.Predicates[1].Name.Text.ToString());
    }

    [Fact]
    public void ParsePredicate_UnclosedFilterBracket_ReportsError_DoesNotThrow()
    {
        var src = "P(Document): this[$.Ok=true";
        var ex = Record.Exception(() => PathLangParser.Parse(src));
        Assert.Null(ex);

        var result = PathLangParser.Parse(src);
        Assert.Contains(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);
    }
}
