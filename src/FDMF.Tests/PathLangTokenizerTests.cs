using FDMF.Core.PathLayer;

namespace FDMF.Tests;

public sealed class PathLangTokenizerTests
{
    [Fact]
    public void Tokenizer_Basic_Path_And_Filter()
    {
        var src = "OwnerCanView(Document): this->Business->Owners[$(Person).CurrentUser=true]";
        var t = new PathLangTokenizer(src);

        AssertToken(t.GetNextToken(), TokenKind.Identifier, "OwnerCanView");
        AssertToken(t.GetNextToken(), TokenKind.LParen, "(");
        AssertToken(t.GetNextToken(), TokenKind.Identifier, "Document");
        AssertToken(t.GetNextToken(), TokenKind.RParen, ")");
        AssertToken(t.GetNextToken(), TokenKind.Colon, ":");

        AssertToken(t.GetNextToken(), TokenKind.KeywordThis, "this");
        AssertToken(t.GetNextToken(), TokenKind.Arrow, "->");
        AssertToken(t.GetNextToken(), TokenKind.Identifier, "Business");
        AssertToken(t.GetNextToken(), TokenKind.Arrow, "->");
        AssertToken(t.GetNextToken(), TokenKind.Identifier, "Owners");

        AssertToken(t.GetNextToken(), TokenKind.LBracket, "[");
        AssertToken(t.GetNextToken(), TokenKind.Dollar, "$");
        AssertToken(t.GetNextToken(), TokenKind.LParen, "(");
        AssertToken(t.GetNextToken(), TokenKind.Identifier, "Person");
        AssertToken(t.GetNextToken(), TokenKind.RParen, ")");
        AssertToken(t.GetNextToken(), TokenKind.Dot, ".");
        AssertToken(t.GetNextToken(), TokenKind.Identifier, "CurrentUser");
        AssertToken(t.GetNextToken(), TokenKind.Equals, "=");
        AssertToken(t.GetNextToken(), TokenKind.KeywordTrue, "true");
        AssertToken(t.GetNextToken(), TokenKind.RBracket, "]");

        AssertToken(t.GetNextToken(), TokenKind.EndOfFile, "");
    }

    [Fact]
    public void Tokenizer_Handles_Whitespace_Newlines_And_Comments()
    {
        var src = """
        // comment
        CanEdit(Document):
            Viewable(this)=true AND this[$.Locked!=false]
        """;

        var t = new PathLangTokenizer(src);

        AssertToken(t.GetNextToken(), TokenKind.Identifier, "CanEdit");
        AssertToken(t.GetNextToken(), TokenKind.LParen, "(");
        AssertToken(t.GetNextToken(), TokenKind.Identifier, "Document");
        AssertToken(t.GetNextToken(), TokenKind.RParen, ")");
        AssertToken(t.GetNextToken(), TokenKind.Colon, ":");

        AssertToken(t.GetNextToken(), TokenKind.Identifier, "Viewable");
        AssertToken(t.GetNextToken(), TokenKind.LParen, "(");
        AssertToken(t.GetNextToken(), TokenKind.KeywordThis, "this");
        AssertToken(t.GetNextToken(), TokenKind.RParen, ")");
        AssertToken(t.GetNextToken(), TokenKind.Equals, "=");
        AssertToken(t.GetNextToken(), TokenKind.KeywordTrue, "true");
        AssertToken(t.GetNextToken(), TokenKind.KeywordAnd, "AND");

        AssertToken(t.GetNextToken(), TokenKind.KeywordThis, "this");
        AssertToken(t.GetNextToken(), TokenKind.LBracket, "[");
        AssertToken(t.GetNextToken(), TokenKind.Dollar, "$");
        AssertToken(t.GetNextToken(), TokenKind.Dot, ".");
        AssertToken(t.GetNextToken(), TokenKind.Identifier, "Locked");
        AssertToken(t.GetNextToken(), TokenKind.NotEquals, "!=");
        AssertToken(t.GetNextToken(), TokenKind.KeywordFalse, "false");
        AssertToken(t.GetNextToken(), TokenKind.RBracket, "]");

        AssertToken(t.GetNextToken(), TokenKind.EndOfFile, "");
    }

    [Fact]
    public void Tokenizer_Parses_String_And_Number_Literals()
    {
        var src = "X(T): this[$.State=\"Active\" AND $.Score=12.5]";
        var t = new PathLangTokenizer(src);

        // X(T):
        AssertToken(t.GetNextToken(), TokenKind.Identifier, "X");
        AssertToken(t.GetNextToken(), TokenKind.LParen, "(");
        AssertToken(t.GetNextToken(), TokenKind.Identifier, "T");
        AssertToken(t.GetNextToken(), TokenKind.RParen, ")");
        AssertToken(t.GetNextToken(), TokenKind.Colon, ":");

        // this[$.State="Active" AND $.Score=12.5]
        AssertToken(t.GetNextToken(), TokenKind.KeywordThis, "this");
        AssertToken(t.GetNextToken(), TokenKind.LBracket, "[");

        AssertToken(t.GetNextToken(), TokenKind.Dollar, "$");
        AssertToken(t.GetNextToken(), TokenKind.Dot, ".");
        AssertToken(t.GetNextToken(), TokenKind.Identifier, "State");
        AssertToken(t.GetNextToken(), TokenKind.Equals, "=");
        AssertToken(t.GetNextToken(), TokenKind.String, "\"Active\"");

        AssertToken(t.GetNextToken(), TokenKind.KeywordAnd, "AND");

        AssertToken(t.GetNextToken(), TokenKind.Dollar, "$");
        AssertToken(t.GetNextToken(), TokenKind.Dot, ".");
        AssertToken(t.GetNextToken(), TokenKind.Identifier, "Score");
        AssertToken(t.GetNextToken(), TokenKind.Equals, "=");
        AssertToken(t.GetNextToken(), TokenKind.Number, "12.5");

        AssertToken(t.GetNextToken(), TokenKind.RBracket, "]");
        AssertToken(t.GetNextToken(), TokenKind.EndOfFile, "");
    }

    private static void AssertToken(Token token, TokenKind kind, string text)
    {
        Assert.Equal(kind, token.Kind);
        Assert.Equal(text, token.Text.ToString());
        Assert.True(token.Line >= 1);
        Assert.True(token.Column >= 1);
    }
}
