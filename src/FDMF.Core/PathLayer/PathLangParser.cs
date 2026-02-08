namespace FDMF.Core.PathLayer;

public sealed class PathLangParser
{
    private readonly PathLangTokenizer _tokenizer;
    private Token _prev;
    private Token _current;
    private readonly List<Token> _lookahead = new();

    public List<PathLangDiagnostic> Diagnostics { get; } = new();

    public static PathLangParseResult Parse(string source)
    {
        return new PathLangParser(source).ParseProgram();
    }

    internal PathLangParser(string source)
    {
        _tokenizer = new PathLangTokenizer(source);
        _current = _tokenizer.GetNextToken();
    }

    public PathLangParseResult ParseProgram()
    {
        Diagnostics.Clear();
        var predicates = new List<AstPredicate>();

        try
        {
            while (_current.Kind != TokenKind.EndOfFile)
            {
                var diagStart = Diagnostics.Count;
                var snap = Snapshot();

                predicates.Add(ParsePredicateDefCore());

                // No explicit delimiter exists in v1; on any error, attempt to resync at a probable next predicate.
                if (_current.Kind != TokenKind.EndOfFile && Diagnostics.Count != diagStart)
                    SynchronizeToNextPredicate();

                // Avoid infinite loops if we failed to make progress.
                if (_current.Kind != TokenKind.EndOfFile && SnapshotEquals(snap, Snapshot()))
                {
                    Report(PathLangDiagnosticSeverity.Error, "Parser made no progress; skipping token", _current);
                    Next();
                }
            }
        }
        catch (Exception ex)
        {
            Report(PathLangDiagnosticSeverity.Error, $"Internal parser error: {ex.Message}", _current);
        }

        return new PathLangParseResult(predicates, new List<PathLangDiagnostic>(Diagnostics));
    }

    private AstPredicate ParsePredicateDefCore()
    {
        var start = _current.Text;

        var name = Expect(TokenKind.Identifier, TokenKind.LParen, TokenKind.Colon);
        Expect(TokenKind.LParen, TokenKind.Identifier, TokenKind.RParen, TokenKind.Colon);
        var typeName = Expect(TokenKind.Identifier, TokenKind.RParen, TokenKind.Colon);
        Expect(TokenKind.RParen, TokenKind.Colon);
        Expect(TokenKind.Colon, TokenKind.EndOfFile);

        var body = ParseExpr();

        return new AstPredicate(new AstIdent(name.Text), new AstIdent(typeName.Text), body, TextView.From(start, _prev.Text));
    }

    private AstExpr ParseExpr() => ParseOr();

    private AstExpr ParseOr()
    {
        var start = _current.Text;

        var left = ParseAnd();
        while (_current.Kind == TokenKind.KeywordOr)
        {
            Next();
            var right = ParseAnd();
            left = new AstLogicalExpr(AstLogicalOp.Or, left, right, TextView.From(start, _prev.Text));
        }
        return left;
    }

    private AstExpr ParseAnd()
    {
        var start = _current.Text;

        var left = ParseTerm();
        while (_current.Kind == TokenKind.KeywordAnd)
        {
            Next();
            var right = ParseTerm();
            left = new AstLogicalExpr(AstLogicalOp.And, left, right, TextView.From(start, _prev.Text));
        }
        return left;
    }


    private AstExpr ParseTerm()
    {
        if (_current.Kind == TokenKind.LParen)
        {
            Next();
            var inner = ParseExpr();
            Expect(TokenKind.RParen, TokenKind.KeywordAnd, TokenKind.KeywordOr, TokenKind.RBracket, TokenKind.EndOfFile);
            return inner;
        }

        // In v1 grammar, a pathExpr must start with this or $.
        // If we see an identifier here, it must be a predicate call.
        if (_current.Kind == TokenKind.Identifier)
        {
            if (ParsePredicateCall(out var call))
                return call;

            return call;
        }

        return ParsePathExpr();
    }

    private bool ParsePredicateCall(out AstExpr call)
    {
        var start = _current.Text;

        var pred = Expect(TokenKind.Identifier);
        Expect(TokenKind.LParen, TokenKind.KeywordThis, TokenKind.Dollar, TokenKind.RParen);
        var arg = ParseArgExpr();
        Expect(TokenKind.RParen, TokenKind.Equals, TokenKind.NotEquals, TokenKind.KeywordAnd, TokenKind.KeywordOr, TokenKind.EndOfFile);

        call = (AstExpr)new AstPredicateCallExpr(new AstIdent(pred.Text), arg, TextView.From(start, _prev.Text));

        // Allow the common (redundant) boolean compare syntax at expression level:
        //   Visible(this)=true
        //   Visible(this)!=false
        // We normalize these to just the call.
        if (_current.Kind == TokenKind.Equals || _current.Kind == TokenKind.NotEquals)
        {
            var opToken = _current;
            var op = ParseCompareOp();

            var lit = ParseLiteral();

            if (lit is not AstBoolLiteral b)
            {
                Report(PathLangDiagnosticSeverity.Error, "Only boolean literals are allowed after predicate calls at expression level", opToken);
                return true;
            }

            // Normalize: call = true OR call != false
            if (op == AstCompareOp.Equals && b.Value)
            {
                Report(PathLangDiagnosticSeverity.Warning, "Redundant '=true' after predicate call is ignored", opToken);
                return true;
            }
            if (op == AstCompareOp.NotEquals && !b.Value)
            {
                Report(PathLangDiagnosticSeverity.Warning, "Redundant '!=false' after predicate call is ignored", opToken);
                return true;
            }

            Report(PathLangDiagnosticSeverity.Error, "Only '=true' and '!=false' are supported after predicate calls (v1)", opToken);
            return true;
        }

        return false;
    }

    private AstExpr ParseArgExpr()
    {
        if (_current.Kind == TokenKind.KeywordThis)
        {
            Next();
            return new AstThisExpr(TextView.From(_prev.Text, _prev.Text));
        }

        if (_current.Kind == TokenKind.Dollar)
        {
            Next();
            return new AstCurrentExpr(TextView.From(_prev.Text, _prev.Text));
        }

        Report(PathLangDiagnosticSeverity.Error, $"Expected argument '$' or 'this' but got {_current.Kind}", _current);
        if (_current.Kind != TokenKind.EndOfFile)
            Next();
        return new AstErrorExpr(TextView.From(_prev.Text, _prev.Text));
    }

    // pathExpr = sourceExpr, { "->", step } , [ filter ] ;
    //eg this->hallo[condition]->repeat(->bello[condition])[condition2]->end
    private AstExpr ParsePathExpr()
    {
        var start = _current.Text;

        var source = ParseSourceExpr(); //this or $

        var steps = new List<AstPathStep>();

        // 0+ -> steps
        while (_current.Kind == TokenKind.Arrow)
        {
            Next();

            steps.Add(ParseStep());
        }

        if (steps.Count > 0)
            source = new AstPathExpr(source, steps, TextView.From(start, _prev.Text));

        // optional trailing filter (this[cond])
        if (_current.Kind == TokenKind.LBracket)
        {
            var startFilter = _current.Text;
            var filter = ParseFilter();
            source = new AstFilterExpr(source, filter, TextView.From(startFilter, _prev.Text));
        }

        return source;
    }

    private AstPathStep ParseStep()
    {
        if (_current.Kind == TokenKind.KeywordRepeat)
        {
            var start = _current.Text;

            Next();
            Expect(TokenKind.LParen);

            var steps = new List<AstPathStep>();
            while (_current.Kind == TokenKind.Arrow)
            {
                Next();

                steps.Add(ParseStep());
            }

            Expect(TokenKind.RParen);

            AstFilter? filter = null;
            if (_current.Kind == TokenKind.LBracket)
                filter = ParseFilter();

            return new AstRepeatStep(steps.ToArray(), TextView.From(start, _prev.Text), filter);
        }
        else
        {
            var start = _current.Text;

            var assoc = Expect(TokenKind.Identifier, TokenKind.LBracket, TokenKind.Arrow, TokenKind.KeywordAnd, TokenKind.KeywordOr, TokenKind.RParen, TokenKind.RBracket, TokenKind.EndOfFile);

            AstFilter? filter = null;
            if (_current.Kind == TokenKind.LBracket)
                filter = ParseFilter();

            return new AstAsoStep(new AstIdent(assoc.Text), TextView.From(start, _prev.Text), filter);
        }
    }

    private AstExpr ParseSourceExpr()
    {
        if (_current.Kind == TokenKind.KeywordThis)
        {
            Next();
            return new AstThisExpr(TextView.From(_prev.Text, _prev.Text));
        }

        if (_current.Kind == TokenKind.Dollar)
        {
            Next();
            return new AstCurrentExpr(TextView.From(_prev.Text, _prev.Text));
        }

        Report(PathLangDiagnosticSeverity.Error, $"Expected 'this' or '$' but got {_current.Kind}", _current);
        if (_current.Kind != TokenKind.EndOfFile)
            Next();

        return new AstErrorExpr(TextView.From(_prev.Text, _prev.Text));
    }

    private AstFilter ParseFilter()
    {
        var start = _current.Text;

        Expect(TokenKind.LBracket, TokenKind.Dollar, TokenKind.Identifier, TokenKind.LParen, TokenKind.RBracket);
        var cond = ParseConditionOr();
        Expect(TokenKind.RBracket, TokenKind.Arrow, TokenKind.KeywordAnd, TokenKind.KeywordOr, TokenKind.EndOfFile);
        return new AstFilter(cond, TextView.From(start, _prev.Text));
    }

    private AstCondition ParseConditionOr()
    {
        var start = _current.Text;

        var left = ParseConditionAnd();
        while (_current.Kind == TokenKind.KeywordOr)
        {
            Next();
            var right = ParseConditionAnd();
            left = new AstConditionBinary(AstConditionOp.Or, left, right, TextView.From(start, _prev.Text));
        }
        return left;
    }

    private AstCondition ParseConditionAnd()
    {
        var start = _current.Text;

        var left = ParseConditionAtom();
        while (_current.Kind == TokenKind.KeywordAnd)
        {
            Next();
            var right = ParseConditionAtom();
            left = new AstConditionBinary(AstConditionOp.And, left, right, TextView.From(start, _prev.Text));
        }
        return left;
    }

    private AstCondition ParseConditionAtom()
    {
        if (_current.Kind == TokenKind.LParen)
        {
            Next();
            var inner = ParseConditionOr();
            Expect(TokenKind.RParen, TokenKind.KeywordAnd, TokenKind.KeywordOr, TokenKind.RBracket, TokenKind.EndOfFile);
            return inner;
        }

        // Predicate compare: Pred($)=true
        if (_current.Kind == TokenKind.Identifier)
        {
            var start = _current.Text;

            var pred = Expect(TokenKind.Identifier);
            Expect(TokenKind.LParen, TokenKind.KeywordThis, TokenKind.Dollar, TokenKind.RParen);
            var arg = ParseArgExpr();
            Expect(TokenKind.RParen, TokenKind.Equals, TokenKind.NotEquals, TokenKind.KeywordAnd, TokenKind.KeywordOr, TokenKind.RBracket, TokenKind.EndOfFile);

            var op = ParseCompareOp();
            var lit = ParseLiteral();

            return new AstPredicateCompareCondition(new AstIdent(pred.Text), arg, op, lit, TextView.From(start, _prev.Text));
        }

        // Field compare: $.Field=... or $(Type).Field=...
        if (_current.Kind != TokenKind.Dollar)
        {
            Report(PathLangDiagnosticSeverity.Error, "Condition must start with '$' or an identifier (predicate call)", _current);
            if (_current.Kind != TokenKind.EndOfFile)
                Next();
            return new AstErrorCondition(TextView.From(_prev.Text, _prev.Text));
        }

        var s = _current.Text;

        Next(); // '$'

        AstIdent? typeGuard = null;
        if (_current.Kind == TokenKind.LParen)
        {
            Next();
            var typeTok = Expect(TokenKind.Identifier);
            typeGuard = new AstIdent(typeTok.Text);
            Expect(TokenKind.RParen, TokenKind.Dot, TokenKind.Identifier, TokenKind.Equals, TokenKind.NotEquals, TokenKind.RBracket);
        }

        Expect(TokenKind.Dot, TokenKind.Identifier, TokenKind.Equals, TokenKind.NotEquals, TokenKind.RBracket);

        var field = Expect(TokenKind.Identifier);
        var cmpOp = ParseCompareOp();
        var value = ParseLiteral();

        return new AstFieldCompareCondition(typeGuard, new AstIdent(field.Text), cmpOp, value, TextView.From(s, _prev.Text));
    }

    private AstCompareOp ParseCompareOp()
    {
        if (_current.Kind == TokenKind.Equals)
        {
            Next();
            return AstCompareOp.Equals;
        }

        if (_current.Kind == TokenKind.NotEquals)
        {
            Next();
            return AstCompareOp.NotEquals;
        }

        Report(PathLangDiagnosticSeverity.Error, $"Expected '=' or '!=' but got {_current.Kind}", _current);
        if (_current.Kind != TokenKind.EndOfFile)
            Next();
        return AstCompareOp.Equals;
    }

    private AstLiteral ParseLiteral()
    {
        if (_current.Kind == TokenKind.KeywordTrue)
        {
            Next();
            return new AstBoolLiteral(true, TextView.From(_prev.Text, _prev.Text));
        }

        if (_current.Kind == TokenKind.KeywordFalse)
        {
            Next();
            return new AstBoolLiteral(false, TextView.From(_prev.Text, _prev.Text));
        }

        if (_current.Kind == TokenKind.Number)
        {
            var tv = _current.Text;
            Next();
            return new AstNumberLiteral(tv, TextView.From(_prev.Text, _prev.Text));
        }

        if (_current.Kind == TokenKind.String)
        {
            var tv = _current.Text;
            Next();
            return new AstStringLiteral(new TextView(tv.Source, tv.Start + 1, tv.Length - 2), TextView.From(_prev.Text, _prev.Text)); //removing start and end quotes
        }

        Report(PathLangDiagnosticSeverity.Error, $"Expected literal but got {_current.Kind}", _current);
        if (_current.Kind != TokenKind.EndOfFile)
            Next();
        return new AstErrorLiteral(TextView.From(_prev.Text, _prev.Text));
    }

    private Token Expect(TokenKind kind, params ReadOnlySpan<TokenKind> recoveryStop)
    {
        if (_current.Kind == kind)
        {
            var ok = _current;
            Next();
            return ok;
        }

        var unexpected = _current;
        Report(PathLangDiagnosticSeverity.Error, $"Expected {kind} but got {unexpected.Kind}", unexpected);

        // Recovery: skip unexpected tokens until we hit either the expected token or a reasonable stop token.
        while (_current.Kind != TokenKind.EndOfFile && _current.Kind != kind && !IsRecoveryStop(_current.Kind, recoveryStop) && !IsStartOfPredicateHere())
            Next();

        if (_current.Kind == kind)
        {
            var ok = _current;
            Next();
            return ok;
        }

        // Insert a missing token.
        return new Token(kind, TextView.Empty(unexpected.Text.Source), unexpected.Line, unexpected.Column);
    }

    private void Next()
    {
        if (_lookahead.Count > 0)
        {
            _prev = _current;
            _current = _lookahead[0];
            _lookahead.RemoveAt(0);
            return;
        }

        _prev = _current;
        _current = _tokenizer.GetNextToken();
    }

    private Token Peek(int offset = 0)
    {
        EnsureLookahead(offset + 1);
        return _lookahead[offset];
    }

    private void Report(PathLangDiagnosticSeverity severity, string message, Token at)
    {
        Diagnostics.Add(new PathLangDiagnostic(severity, message, at.Line, at.Column, at.Text));
    }

    private static bool IsRecoveryStop(TokenKind kind, ReadOnlySpan<TokenKind> extra)
    {
        if (kind is TokenKind.EndOfFile or TokenKind.RParen or TokenKind.RBracket or TokenKind.Colon or TokenKind.Comma)
            return true;
        if (kind is TokenKind.KeywordAnd or TokenKind.KeywordOr)
            return true;
        if (kind is TokenKind.Arrow)
            return true;

        for (int i = 0; i < extra.Length; i++)
        {
            if (extra[i] == kind)
                return true;
        }
        return false;
    }

    private void SynchronizeToNextPredicate()
    {
        // Best-effort: scan forward until we see: ident '(' ident ')' ':'
        // This is intentionally conservative; v1 has no explicit predicate delimiter.
        while (_current.Kind != TokenKind.EndOfFile)
        {
            if (IsStartOfPredicateHere())
                return;
            Next();
        }
    }

    private bool IsStartOfPredicateHere()
    {
        if (_current.Kind != TokenKind.Identifier)
            return false;

        var t1 = Peek(0);
        var t2 = Peek(1);
        var t3 = Peek(2);
        var t4 = Peek(3);

        return t1.Kind == TokenKind.LParen && t2.Kind == TokenKind.Identifier && t3.Kind == TokenKind.RParen && t4.Kind == TokenKind.Colon;
    }

    private void EnsureLookahead(int count)
    {
        while (_lookahead.Count < count)
            _lookahead.Add(_tokenizer.GetNextToken());
    }

    private readonly record struct Snap(TokenKind Kind, int Line, int Column, int Start, int Length);

    private Snap Snapshot()
    {
        return new Snap(_current.Kind, _current.Line, _current.Column, _current.Text.Start, _current.Text.Length);
    }

    private static bool SnapshotEquals(Snap a, Snap b)
    {
        return a.Kind == b.Kind && a.Line == b.Line && a.Column == b.Column && a.Start == b.Start && a.Length == b.Length;
    }
}
