using System.Diagnostics;

namespace FDMF.Core.PathLayer;

/// <summary>
/// A non-allocating view into the original PathLang source text.
/// </summary>
public readonly record struct TextView(string Source, int Start, int Length)
{
    public ReadOnlySpan<char> Span => Source.AsSpan(Start, Length);

    public bool IsEmpty => Length == 0;

    public override string ToString() => Span.ToString();

    public static TextView Empty(string source) => new(source, 0, 0);

    public static TextView From(TextView start, TextView end)
    {
        Debug.Assert(start.Source == end.Source);

        return new TextView(start.Source, start.Start, (end.Start + end.Length) - start.Start);
    }
}

public enum TokenKind : byte
{
    EndOfFile,

    Identifier,
    Number,
    String,

    KeywordThis,
    KeywordRepeat,
    KeywordAnd,
    KeywordOr,
    KeywordTrue,
    KeywordFalse,

    Dollar,

    Arrow,      // ->
    Dot,        // .
    Comma,      // ,
    Colon,      // :

    LParen,     // (
    RParen,     // )
    LBracket,   // [
    RBracket,   // ]

    Equals,     // =
    NotEquals,  // !=
}

public readonly record struct Token(TokenKind Kind, TextView Text, int Line, int Column)
{
    public override string ToString() => $"{Kind} '{Text}' ({Line}:{Column})";
}

public sealed class PathLangTokenizer
{
    private readonly string _source;
    private int _pos;
    private int _line;
    private int _col;

    public PathLangTokenizer(string source)
    {
        _source = source ?? string.Empty;
        _pos = 0;
        _line = 1;
        _col = 1;
    }

    public Token GetNextToken()
    {
        SkipWhitespaceAndComments();

        if (_pos >= _source.Length)
            return new Token(TokenKind.EndOfFile, TextView.Empty(_source), _line, _col);

        var startPos = _pos;
        var startLine = _line;
        var startCol = _col;

        var c = _source[_pos];

        // Two-character operators
        if (c == '-' && Peek(1) == '>')
        {
            Advance(2);
            return MakeToken(TokenKind.Arrow, startPos, startLine, startCol);
        }

        if (c == '!' && Peek(1) == '=')
        {
            Advance(2);
            return MakeToken(TokenKind.NotEquals, startPos, startLine, startCol);
        }

        // Single-character tokens
        switch (c)
        {
            case '$':
                Advance(1);
                return MakeToken(TokenKind.Dollar, startPos, startLine, startCol);
            case '.':
                Advance(1);
                return MakeToken(TokenKind.Dot, startPos, startLine, startCol);
            case ',':
                Advance(1);
                return MakeToken(TokenKind.Comma, startPos, startLine, startCol);
            case ':':
                Advance(1);
                return MakeToken(TokenKind.Colon, startPos, startLine, startCol);
            case '(':
                Advance(1);
                return MakeToken(TokenKind.LParen, startPos, startLine, startCol);
            case ')':
                Advance(1);
                return MakeToken(TokenKind.RParen, startPos, startLine, startCol);
            case '[':
                Advance(1);
                return MakeToken(TokenKind.LBracket, startPos, startLine, startCol);
            case ']':
                Advance(1);
                return MakeToken(TokenKind.RBracket, startPos, startLine, startCol);
            case '=':
                Advance(1);
                return MakeToken(TokenKind.Equals, startPos, startLine, startCol);
        }

        // String literals
        if (c == '"')
        {
            Advance(1); // opening quote

            while (_pos < _source.Length)
            {
                var ch = _source[_pos];

                if (ch == '\\')
                {
                    // escape sequence, consume '\\' + next char (if any)
                    Advance(1);
                    if (_pos < _source.Length)
                        Advance(1);
                    continue;
                }

                if (ch == '"')
                {
                    Advance(1); // closing quote
                    break;
                }

                // allow newlines inside strings for now (line/col tracking still applies)
                Advance(1);
            }

            return MakeToken(TokenKind.String, startPos, startLine, startCol);
        }

        // Numbers
        if (char.IsAsciiDigit(c))
        {
            Advance(1);
            while (_pos < _source.Length && char.IsAsciiDigit(_source[_pos]))
                Advance(1);

            // fractional part
            if (_pos < _source.Length && _source[_pos] == '.' && char.IsAsciiDigit(Peek(1)))
            {
                Advance(1); // '.'
                while (_pos < _source.Length && char.IsAsciiDigit(_source[_pos]))
                    Advance(1);
            }

            return MakeToken(TokenKind.Number, startPos, startLine, startCol);
        }

        // Identifiers / keywords
        if (IsIdentStart(c))
        {
            Advance(1);
            while (_pos < _source.Length && IsIdentPart(_source[_pos]))
                Advance(1);

            var tv = new TextView(_source, startPos, _pos - startPos);
            var kind = KeywordOrIdentifier(tv);
            return new Token(kind, tv, startLine, startCol);
        }

        // Unknown character: return as Identifier-sized token for now (parser can error)
        Advance(1);
        return MakeToken(TokenKind.Identifier, startPos, startLine, startCol);
    }

    private Token MakeToken(TokenKind kind, int startPos, int startLine, int startCol)
    {
        return new Token(kind, new TextView(_source, startPos, _pos - startPos), startLine, startCol);
    }

    private char Peek(int offset)
    {
        var idx = _pos + offset;
        return idx >= 0 && idx < _source.Length ? _source[idx] : '\0';
    }

    private void Advance(int count)
    {
        for (int i = 0; i < count && _pos < _source.Length; i++)
        {
            var c = _source[_pos++];
            if (c == '\n')
            {
                _line++;
                _col = 1;
            }
            else
            {
                _col++;
            }
        }
    }

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _source.Length)
        {
            var c = _source[_pos];

            // whitespace
            if (char.IsWhiteSpace(c))
            {
                Advance(1);
                continue;
            }

            // line comment: //...
            if (c == '/' && Peek(1) == '/')
            {
                Advance(2);
                while (_pos < _source.Length && _source[_pos] != '\n')
                    Advance(1);
                continue;
            }

            break;
        }
    }

    private static bool IsIdentStart(char c)
    {
        return char.IsAsciiLetter(c) || c == '_';
    }

    private static bool IsIdentPart(char c)
    {
        return char.IsAsciiLetterOrDigit(c) || c == '_';
    }

    private static TokenKind KeywordOrIdentifier(TextView tv)
    {
        // Keywords are case sensitive.
        var s = tv.Span;

        return s switch
        {
            "this" => TokenKind.KeywordThis,
            "repeat" => TokenKind.KeywordRepeat,
            "AND" => TokenKind.KeywordAnd,
            "OR" => TokenKind.KeywordOr,
            "true" => TokenKind.KeywordTrue,
            "false" => TokenKind.KeywordFalse,
            _ => TokenKind.Identifier,
        };
    }
}
