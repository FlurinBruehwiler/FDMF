using FDMF.Core.Database;
using FDMF.Core.PathLayer;

namespace FDMF.Tests;

//todo assert compare against real output
public sealed class PathLangParserTests(ITestOutputHelper outputHelper)
{
    private void AssertEqual(string expected, string actual)
    {
        if (expected == actual)
        {
            Assert.Equal(expected, actual);
            return;
        }

        outputHelper.WriteLine($"Expected: {expected}");
        outputHelper.WriteLine("");
        outputHelper.WriteLine($"Actual: {actual}");

        outputHelper.WriteLine("\nLine by Line:");

        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');

        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            outputHelper.WriteLine("e: " + expectedLines.GetOrDefault(i, ""));
            outputHelper.WriteLine("a: " + actualLines.GetOrDefault(i, ""));
            outputHelper.WriteLine("");
        }

        Assert.Fail("Not equal, see output");
    }

    [Fact]
    public void ParsePredicate_SimpleTraversalWithFieldGuard()
    {
        var src = "OwnerCanView(Document): this->Business->Owners[$(Person).CurrentUser=true]";
        var result = PathLangParser.Parse(src);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var actual = PathLangAstPrinter.PrintProgram(result.Predicates, false);
        var expected = """
                       Predicate OwnerCanView(Document)
                         Body:
                           PathExpr
                             Source:
                               This
                             Steps:
                               -> Business
                               -> Owners
                                 Filter:
                                   [
                                     FieldCompare Equals
                                       TypeGuard: Person
                                       Field: CurrentUser
                                       Value:
                                         Bool true
                                   ]
                       """;

        AssertEqual(expected, actual);
    }

    [Fact]
    public void Parse_Repeat_Predicate()
    {
      var src = "RepeatPredicate(Document): this->hallo[$.someProp=true]->repeat(->bello[$.someProp2=true])[$.someProp3=true]->end";
      var result = PathLangParser.Parse(src);
      Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

      var actual = PathLangAstPrinter.PrintProgram(result.Predicates, false);
      var expected = """
                     Predicate RepeatPredicate(Document)
                       Body:
                         PathExpr
                           Source:
                             This
                           Steps:
                             -> hallo
                               Filter:
                                 [
                                   FieldCompare Equals
                                     Field: someProp
                                     Value:
                                       Bool true
                                 ]
                             -> Repeat
                                   Steps:
                                     -> bello
                                       Filter:
                                         [
                                           FieldCompare Equals
                                             Field: someProp2
                                             Value:
                                               Bool true
                                         ]
                               Filter:
                                 [
                                   FieldCompare Equals
                                     Field: someProp3
                                     Value:
                                       Bool true
                                 ]
                             -> end
                     """;

      AssertEqual(expected, actual);
    }

    [Fact]
    public void ParsePredicate_LogicalAndOr_And_PredicateCallCompare()
    {
        var src = "CanEdit(Document): (Viewable(this)=true AND Editable(this)=true) OR OwnerCanView(this)=true";
        var result = PathLangParser.Parse(src);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var actual = PathLangAstPrinter.PrintProgram(result.Predicates, false);
        var expected = """
                       Predicate CanEdit(Document)
                         Body:
                           Logical Or
                             Left:
                               Logical And
                                 Left:
                                   PredicateCall Viewable
                                     Arg:
                                       This
                                 Right:
                                   PredicateCall Editable
                                     Arg:
                                       This
                             Right:
                               PredicateCall OwnerCanView
                                 Arg:
                                   This
                       """;

        AssertEqual(expected, actual);
    }

    [Fact]
    public void ParsePredicate_FilterPredicateCall()
    {
        var src = "TaskViewable(Task): this->Document[Viewable($)=true]";
        var result = PathLangParser.Parse(src);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PathLangDiagnosticSeverity.Error);

        var actual = PathLangAstPrinter.PrintProgram(result.Predicates, false);
        var expected = """
                       Predicate TaskViewable(Task)
                         Body:
                           PathExpr
                             Source:
                               This
                             Steps:
                               -> Document
                                 Filter:
                                   [
                                     PredicateCompare Equals Viewable
                                       Arg:
                                         Current ($)
                                       Value:
                                         Bool true
                                   ]
                       """;

        AssertEqual(expected, actual);
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
