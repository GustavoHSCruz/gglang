using System.Text;

namespace ggLang.Compiler.Lexer;

/// <summary>
/// Lexical analyzer (lexer/scanner) for the ggLang language.
/// Converts source code into a sequence of tokens.
/// </summary>
public sealed class GgLexer
{
    private readonly string _source;
    private readonly string _fileName;
    private int _position;
    private int _line;
    private int _column;
    private readonly List<Token> _tokens = [];
    private readonly List<string> _errors = [];

    /// <summary>
    /// Keyword-to-token-type mapping.
    /// </summary>
    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        ["module"] = TokenType.Module,
        ["import"] = TokenType.Import,
        ["class"] = TokenType.Class,
        ["interface"] = TokenType.Interface,
        ["struct"] = TokenType.Struct,
        ["enum"] = TokenType.Enum,
        ["var"] = TokenType.Var,
        ["if"] = TokenType.If,
        ["else"] = TokenType.Else,
        ["while"] = TokenType.While,
        ["for"] = TokenType.For,
        ["foreach"] = TokenType.ForEach,
        ["in"] = TokenType.In,
        ["return"] = TokenType.Return,
        ["break"] = TokenType.Break,
        ["continue"] = TokenType.Continue,
        ["new"] = TokenType.New,
        ["this"] = TokenType.This,
        ["base"] = TokenType.Base,
        ["null"] = TokenType.Null,
        ["true"] = TokenType.True,
        ["false"] = TokenType.False,
        ["static"] = TokenType.Static,
        ["public"] = TokenType.Public,
        ["private"] = TokenType.Private,
        ["protected"] = TokenType.Protected,
        ["abstract"] = TokenType.Abstract,
        ["virtual"] = TokenType.Virtual,
        ["override"] = TokenType.Override,
        ["sealed"] = TokenType.Sealed,
        ["readonly"] = TokenType.Readonly,
        ["const"] = TokenType.Const,
        ["as"] = TokenType.As,
        ["is"] = TokenType.Is,
        ["match"] = TokenType.Match,
        ["case"] = TokenType.Case,
        ["default"] = TokenType.Default,
        ["int"] = TokenType.Int,
        ["float"] = TokenType.Float,
        ["double"] = TokenType.Double,
        ["bool"] = TokenType.Bool,
        ["char"] = TokenType.Char,
        ["string"] = TokenType.StringType,
        ["void"] = TokenType.Void,
        ["long"] = TokenType.Long,
        ["byte"] = TokenType.Byte,
    };

    public IReadOnlyList<string> Errors => _errors;
    public bool HasErrors => _errors.Count > 0;

    public GgLexer(string source, string fileName = "")
    {
        _source = source;
        _fileName = fileName;
        _position = 0;
        _line = 1;
        _column = 1;
    }

    /// <summary>
    /// Performs full lexical analysis and returns all tokens.
    /// </summary>
    public List<Token> Tokenize()
    {
        _tokens.Clear();
        _errors.Clear();

        while (!IsAtEnd())
        {
            SkipWhitespaceAndComments();

            if (IsAtEnd())
                break;

            var token = ScanToken();
            if (token != null)
                _tokens.Add(token);
        }

        _tokens.Add(MakeToken(TokenType.EndOfFile, ""));
        return _tokens;
    }

    private Token? ScanToken()
    {
        var ch = Current();

        // Strings
        if (ch == '"')
            return ScanString();

        // Characters
        if (ch == '\'')
            return ScanChar();

        // Numbers
        if (char.IsDigit(ch))
            return ScanNumber();

        // Identifiers and keywords
        if (char.IsLetter(ch) || ch == '_')
            return ScanIdentifierOrKeyword();

        // Operators and delimiters
        return ScanOperatorOrDelimiter();
    }

    #region Scanning Methods

    private Token ScanString()
    {
        var startLine = _line;
        var startCol = _column;
        Advance(); // consume opening '"'

        var sb = new StringBuilder();
        while (!IsAtEnd() && Current() != '"')
        {
            if (Current() == '\\')
            {
                Advance();
                if (IsAtEnd())
                {
                    _errors.Add($"({startLine}:{startCol}): unterminated string literal — the file ended inside an escape sequence. Add a closing '\"' to complete the string.");
                    return new Token(TokenType.Invalid, sb.ToString(), startLine, startCol, _fileName);
                }
                sb.Append(Current() switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '\\' => '\\',
                    '"' => '"',
                    '0' => '\0',
                    _ => Current()
                });
            }
            else
            {
                if (Current() == '\n')
                {
                    _line++;
                    _column = 0;
                }
                sb.Append(Current());
            }
            Advance();
        }

        if (IsAtEnd())
        {
            _errors.Add($"({startLine}:{startCol}): unterminated string literal — missing closing '\"'. Ensure the string started at line {startLine} is properly closed.");
            return new Token(TokenType.Invalid, sb.ToString(), startLine, startCol, _fileName);
        }

        Advance(); // consume closing '"'
        return new Token(TokenType.StringLiteral, sb.ToString(), startLine, startCol, _fileName);
    }

    private Token ScanChar()
    {
        var startLine = _line;
        var startCol = _column;
        Advance(); // consume '\''

        if (IsAtEnd())
        {
            _errors.Add($"({startLine}:{startCol}): empty character literal — a char literal must contain exactly one character, e.g. 'a' or '\\n'.");
            return new Token(TokenType.Invalid, "", startLine, startCol, _fileName);
        }

        // Check for empty char literal: ''
        if (Current() == '\'')
        {
            Advance(); // consume closing '\''
            _errors.Add($"({startLine}:{startCol}): empty character literal — a char literal must contain exactly one character, e.g. 'a' or '\\n'.");
            return new Token(TokenType.Invalid, "", startLine, startCol, _fileName);
        }

        char value;
        if (Current() == '\\')
        {
            Advance();
            if (IsAtEnd())
            {
                _errors.Add($"({startLine}:{startCol}): unterminated character literal — the file ended inside an escape sequence. Add a closing \"'\" to complete the char literal.");
                return new Token(TokenType.Invalid, "", startLine, startCol, _fileName);
            }
            value = Current() switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '\\' => '\\',
                '\'' => '\'',
                '0' => '\0',
                _ => Current()
            };
        }
        else
        {
            value = Current();
        }

        Advance();

        if (IsAtEnd())
        {
            _errors.Add($"({startLine}:{startCol}): unterminated character literal — missing closing \"'\". A char literal must be a single character enclosed in single quotes, e.g. 'x'.");
            return new Token(TokenType.Invalid, value.ToString(), startLine, startCol, _fileName);
        }

        // If the next char is NOT a closing quote, the literal has multiple characters
        if (Current() != '\'')
        {
            // Consume remaining characters until closing quote or end of line
            var sb = new StringBuilder();
            sb.Append(value);
            while (!IsAtEnd() && Current() != '\'' && Current() != '\n' && Current() != ';')
            {
                sb.Append(Current());
                Advance();
            }

            if (!IsAtEnd() && Current() == '\'')
            {
                Advance(); // consume closing '\''
                _errors.Add($"({startLine}:{startCol}): character literal contains too many characters — a char literal must be exactly one character (e.g. 'a'). Did you mean to use a string? Use double quotes instead: \"{sb}\".");
            }
            else
            {
                _errors.Add($"({startLine}:{startCol}): unterminated character literal — missing closing \"'\". A char literal must be a single character enclosed in single quotes, e.g. 'x'.");
            }

            return new Token(TokenType.Invalid, sb.ToString(), startLine, startCol, _fileName);
        }

        Advance(); // consume closing '\''
        return new Token(TokenType.CharLiteral, value.ToString(), startLine, startCol, _fileName);
    }

    private Token ScanNumber()
    {
        var startLine = _line;
        var startCol = _column;
        var sb = new StringBuilder();
        var isFloat = false;

        while (!IsAtEnd() && (char.IsDigit(Current()) || Current() == '.' || Current() == '_'))
        {
            if (Current() == '.')
            {
                // Check if it's a member access (e.g. 42.toString())
                if (isFloat || (_position + 1 < _source.Length && !char.IsDigit(_source[_position + 1])))
                    break;
                isFloat = true;
            }

            if (Current() != '_')
                sb.Append(Current());

            Advance();
        }

        // Suffix: f/F (float), d/D (double), L (long)
        if (!IsAtEnd() && (Current() == 'f' || Current() == 'F'))
        {
            isFloat = true;
            sb.Append(Current());
            Advance();
        }
        else if (!IsAtEnd() && (Current() == 'd' || Current() == 'D'))
        {
            isFloat = true;
            sb.Append(Current());
            Advance();
        }
        else if (!IsAtEnd() && (Current() == 'l' || Current() == 'L'))
        {
            sb.Append(Current());
            Advance();
        }

        return new Token(
            isFloat ? TokenType.FloatLiteral : TokenType.IntegerLiteral,
            sb.ToString(),
            startLine, startCol, _fileName
        );
    }

    private Token ScanIdentifierOrKeyword()
    {
        var startLine = _line;
        var startCol = _column;
        var sb = new StringBuilder();

        while (!IsAtEnd() && (char.IsLetterOrDigit(Current()) || Current() == '_'))
        {
            sb.Append(Current());
            Advance();
        }

        var text = sb.ToString();

        // Check if it's a keyword
        if (Keywords.TryGetValue(text, out var keywordType))
        {
            return new Token(keywordType, text, startLine, startCol, _fileName);
        }

        return new Token(TokenType.Identifier, text, startLine, startCol, _fileName);
    }

    private Token ScanOperatorOrDelimiter()
    {
        var startLine = _line;
        var startCol = _column;
        var ch = Current();
        Advance();

        switch (ch)
        {
            case '+':
                if (!IsAtEnd() && Current() == '+') { Advance(); return MakeToken(TokenType.PlusPlus, "++", startLine, startCol); }
                if (!IsAtEnd() && Current() == '=') { Advance(); return MakeToken(TokenType.PlusEquals, "+=", startLine, startCol); }
                return MakeToken(TokenType.Plus, "+", startLine, startCol);

            case '-':
                if (!IsAtEnd() && Current() == '-') { Advance(); return MakeToken(TokenType.MinusMinus, "--", startLine, startCol); }
                if (!IsAtEnd() && Current() == '=') { Advance(); return MakeToken(TokenType.MinusEquals, "-=", startLine, startCol); }
                return MakeToken(TokenType.Minus, "-", startLine, startCol);

            case '*':
                if (!IsAtEnd() && Current() == '=') { Advance(); return MakeToken(TokenType.StarEquals, "*=", startLine, startCol); }
                return MakeToken(TokenType.Star, "*", startLine, startCol);

            case '/':
                if (!IsAtEnd() && Current() == '=') { Advance(); return MakeToken(TokenType.SlashEquals, "/=", startLine, startCol); }
                return MakeToken(TokenType.Slash, "/", startLine, startCol);

            case '%': return MakeToken(TokenType.Percent, "%", startLine, startCol);

            case '=':
                if (!IsAtEnd() && Current() == '=') { Advance(); return MakeToken(TokenType.EqualsEquals, "==", startLine, startCol); }
                if (!IsAtEnd() && Current() == '>') { Advance(); return MakeToken(TokenType.Arrow, "=>", startLine, startCol); }
                return MakeToken(TokenType.Equals, "=", startLine, startCol);

            case '!':
                if (!IsAtEnd() && Current() == '=') { Advance(); return MakeToken(TokenType.BangEquals, "!=", startLine, startCol); }
                return MakeToken(TokenType.Bang, "!", startLine, startCol);

            case '<':
                if (!IsAtEnd() && Current() == '=') { Advance(); return MakeToken(TokenType.LessEquals, "<=", startLine, startCol); }
                if (!IsAtEnd() && Current() == '<') { Advance(); return MakeToken(TokenType.LessLess, "<<", startLine, startCol); }
                return MakeToken(TokenType.Less, "<", startLine, startCol);

            case '>':
                if (!IsAtEnd() && Current() == '=') { Advance(); return MakeToken(TokenType.GreaterEquals, ">=", startLine, startCol); }
                if (!IsAtEnd() && Current() == '>') { Advance(); return MakeToken(TokenType.GreaterGreater, ">>", startLine, startCol); }
                return MakeToken(TokenType.Greater, ">", startLine, startCol);

            case '&':
                if (!IsAtEnd() && Current() == '&') { Advance(); return MakeToken(TokenType.AmpersandAmpersand, "&&", startLine, startCol); }
                return MakeToken(TokenType.Ampersand, "&", startLine, startCol);

            case '|':
                if (!IsAtEnd() && Current() == '|') { Advance(); return MakeToken(TokenType.PipePipe, "||", startLine, startCol); }
                return MakeToken(TokenType.Pipe, "|", startLine, startCol);

            case '^': return MakeToken(TokenType.Caret, "^", startLine, startCol);
            case '~': return MakeToken(TokenType.Tilde, "~", startLine, startCol);
            case '@': return MakeToken(TokenType.At, "@", startLine, startCol);

            case '(': return MakeToken(TokenType.LeftParen, "(", startLine, startCol);
            case ')': return MakeToken(TokenType.RightParen, ")", startLine, startCol);
            case '{': return MakeToken(TokenType.LeftBrace, "{", startLine, startCol);
            case '}': return MakeToken(TokenType.RightBrace, "}", startLine, startCol);
            case '[': return MakeToken(TokenType.LeftBracket, "[", startLine, startCol);
            case ']': return MakeToken(TokenType.RightBracket, "]", startLine, startCol);
            case ';': return MakeToken(TokenType.Semicolon, ";", startLine, startCol);
            case ':': return MakeToken(TokenType.Colon, ":", startLine, startCol);
            case ',': return MakeToken(TokenType.Comma, ",", startLine, startCol);
            case '.': return MakeToken(TokenType.Dot, ".", startLine, startCol);
            case '?': return MakeToken(TokenType.QuestionMark, "?", startLine, startCol);

            default:
                _errors.Add($"({startLine}:{startCol}): unexpected character '{ch}' (U+{(int)ch:X4}) — this character is not valid in ggLang source code. Check for accidental special characters or encoding issues.");
                return new Token(TokenType.Invalid, ch.ToString(), startLine, startCol, _fileName);
        }
    }

    #endregion

    #region Helpers

    private void SkipWhitespaceAndComments()
    {
        while (!IsAtEnd())
        {
            var ch = Current();

            // Whitespace
            if (char.IsWhiteSpace(ch))
            {
                if (ch == '\n')
                {
                    _line++;
                    _column = 0;
                }
                Advance();
                continue;
            }

            // Line comment: //
            if (ch == '/' && _position + 1 < _source.Length && _source[_position + 1] == '/')
            {
                while (!IsAtEnd() && Current() != '\n')
                    Advance();
                continue;
            }

            // Block comment: /* */
            if (ch == '/' && _position + 1 < _source.Length && _source[_position + 1] == '*')
            {
                Advance(); // /
                Advance(); // *
                while (!IsAtEnd())
                {
                    if (Current() == '\n')
                    {
                        _line++;
                        _column = 0;
                    }
                    if (Current() == '*' && _position + 1 < _source.Length && _source[_position + 1] == '/')
                    {
                        Advance(); // *
                        Advance(); // /
                        break;
                    }
                    Advance();
                }
                continue;
            }

            break;
        }
    }

    private bool IsAtEnd() => _position >= _source.Length;

    private char Current() => _position < _source.Length ? _source[_position] : '\0';

    private void Advance()
    {
        _position++;
        _column++;
    }

    private Token MakeToken(TokenType type, string value, int? line = null, int? col = null)
    {
        return new Token(type, value, line ?? _line, col ?? _column, _fileName);
    }

    #endregion
}
