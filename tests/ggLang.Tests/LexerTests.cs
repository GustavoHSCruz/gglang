using ggLang.Compiler.Lexer;

namespace ggLang.Tests;

/// <summary>
/// Unit tests for the ggLang lexer.
/// </summary>
public class LexerTests
{
    [Fact]
    public void TokenizesSimpleClass()
    {
        var source = "class Hello { }";
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();

        Assert.False(lexer.HasErrors);
        Assert.Contains(tokens, t => t.Type == TokenType.Class);
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "Hello");
        Assert.Contains(tokens, t => t.Type == TokenType.LeftBrace);
        Assert.Contains(tokens, t => t.Type == TokenType.RightBrace);
    }

    [Fact]
    public void TokenizesTypedVariableDeclaration()
    {
        var source = "int x = 42;";
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();

        Assert.False(lexer.HasErrors);
        Assert.Contains(tokens, t => t.Type == TokenType.Int);
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "x");
        Assert.Contains(tokens, t => t.Type == TokenType.Equals);
        Assert.Contains(tokens, t => t.Type == TokenType.IntegerLiteral && t.Value == "42");
    }

    [Fact]
    public void TokenizesVarDeclaration()
    {
        var source = "var x = 42;";
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();

        Assert.False(lexer.HasErrors);
        Assert.Contains(tokens, t => t.Type == TokenType.Var);
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "x");
    }

    [Fact]
    public void TokenizesMethodSignature()
    {
        // C#-style: int add(int a, int b)
        var source = "int add(int a, int b)";
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();

        Assert.False(lexer.HasErrors);
        Assert.Contains(tokens, t => t.Type == TokenType.Int);
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "add");
        Assert.Contains(tokens, t => t.Type == TokenType.LeftParen);
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "a");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "b");
    }

    [Fact]
    public void TokenizesStringLiteral()
    {
        var source = "\"Hello, World!\"";
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();

        Assert.False(lexer.HasErrors);
        Assert.Contains(tokens, t => t.Type == TokenType.StringLiteral && t.Value == "Hello, World!");
    }

    [Fact]
    public void TokenizesOperators()
    {
        var source = "+ - * / == != <= >= && || ++ --";
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();

        Assert.False(lexer.HasErrors);
        Assert.Contains(tokens, t => t.Type == TokenType.Plus);
        Assert.Contains(tokens, t => t.Type == TokenType.Minus);
        Assert.Contains(tokens, t => t.Type == TokenType.Star);
        Assert.Contains(tokens, t => t.Type == TokenType.Slash);
        Assert.Contains(tokens, t => t.Type == TokenType.EqualsEquals);
        Assert.Contains(tokens, t => t.Type == TokenType.BangEquals);
        Assert.Contains(tokens, t => t.Type == TokenType.LessEquals);
        Assert.Contains(tokens, t => t.Type == TokenType.GreaterEquals);
        Assert.Contains(tokens, t => t.Type == TokenType.AmpersandAmpersand);
        Assert.Contains(tokens, t => t.Type == TokenType.PipePipe);
        Assert.Contains(tokens, t => t.Type == TokenType.PlusPlus);
        Assert.Contains(tokens, t => t.Type == TokenType.MinusMinus);
    }

    [Fact]
    public void TokenizesKeywords()
    {
        var source = "class interface enum if else while for return new this base";
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();

        Assert.False(lexer.HasErrors);
        Assert.Contains(tokens, t => t.Type == TokenType.Class);
        Assert.Contains(tokens, t => t.Type == TokenType.Interface);
        Assert.Contains(tokens, t => t.Type == TokenType.Enum);
        Assert.Contains(tokens, t => t.Type == TokenType.If);
        Assert.Contains(tokens, t => t.Type == TokenType.Else);
        Assert.Contains(tokens, t => t.Type == TokenType.While);
        Assert.Contains(tokens, t => t.Type == TokenType.For);
        Assert.Contains(tokens, t => t.Type == TokenType.Return);
        Assert.Contains(tokens, t => t.Type == TokenType.New);
        Assert.Contains(tokens, t => t.Type == TokenType.This);
        Assert.Contains(tokens, t => t.Type == TokenType.Base);
    }

    [Fact]
    public void TokenizesAccessModifiers()
    {
        var source = "public private protected static abstract virtual override";
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();

        Assert.False(lexer.HasErrors);
        Assert.Contains(tokens, t => t.Type == TokenType.Public);
        Assert.Contains(tokens, t => t.Type == TokenType.Private);
        Assert.Contains(tokens, t => t.Type == TokenType.Protected);
        Assert.Contains(tokens, t => t.Type == TokenType.Static);
        Assert.Contains(tokens, t => t.Type == TokenType.Abstract);
        Assert.Contains(tokens, t => t.Type == TokenType.Virtual);
        Assert.Contains(tokens, t => t.Type == TokenType.Override);
    }

    [Fact]
    public void TokenizesNumericLiterals()
    {
        var source = "42 3.14 0xFF 0b1010";
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();

        Assert.False(lexer.HasErrors);
        Assert.Contains(tokens, t => t.Type == TokenType.IntegerLiteral);
        Assert.Contains(tokens, t => t.Type == TokenType.FloatLiteral);
    }

    [Fact]
    public void SkipsComments()
    {
        var source = "int x = 5; // this is a comment\n/* block comment */ int y = 10;";
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();

        Assert.False(lexer.HasErrors);
        // Comments should not appear as tokens
        Assert.DoesNotContain(tokens, t => t.Value == "this");
        Assert.DoesNotContain(tokens, t => t.Value == "block");
    }

    [Fact]
    public void ReportsErrorOnUnterminatedString()
    {
        var source = "\"hello";
        var lexer = new GgLexer(source);
        lexer.Tokenize();

        Assert.True(lexer.HasErrors);
    }

    [Fact]
    public void TokenizesValidCharLiteral()
    {
        var source = "'a'";
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();

        Assert.False(lexer.HasErrors);
        Assert.Contains(tokens, t => t.Type == TokenType.CharLiteral && t.Value == "a");
    }

    [Fact]
    public void TokenizesEscapedCharLiteral()
    {
        var source = "'\\n'";
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();

        Assert.False(lexer.HasErrors);
        Assert.Contains(tokens, t => t.Type == TokenType.CharLiteral);
    }

    [Fact]
    public void MultiCharLiteral_ReportsError()
    {
        var source = "'teste'";
        var lexer = new GgLexer(source);
        lexer.Tokenize();

        Assert.True(lexer.HasErrors);
        Assert.Contains(lexer.Errors, e => e.Contains("too many characters"));
    }

    [Fact]
    public void EmptyCharLiteral_ReportsError()
    {
        var source = "''";
        var lexer = new GgLexer(source);
        lexer.Tokenize();

        Assert.True(lexer.HasErrors);
        Assert.Contains(lexer.Errors, e => e.Contains("empty character literal"));
    }
}
