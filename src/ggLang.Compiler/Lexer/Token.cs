namespace ggLang.Compiler.Lexer;

/// <summary>
/// Represents a token produced by the lexer.
/// </summary>
public sealed record Token(
    TokenType Type,
    string Value,
    int Line,
    int Column,
    string FileName = ""
)
{
    /// <summary>
    /// Formatted position for error messages.
    /// </summary>
    public string Position => string.IsNullOrEmpty(FileName)
        ? $"({Line}:{Column})"
        : $"{FileName}({Line}:{Column})";

    public override string ToString() => $"{Type} '{Value}' at {Position}";

    /// <summary>
    /// Checks if the token is of a specific type.
    /// </summary>
    public bool Is(TokenType type) => Type == type;

    /// <summary>
    /// Checks if the token is a primitive type keyword.
    /// </summary>
    public bool IsPrimitiveType() => Type is TokenType.Int or TokenType.Float
        or TokenType.Double or TokenType.Bool or TokenType.Char
        or TokenType.StringType or TokenType.Void or TokenType.Long
        or TokenType.Byte;

    /// <summary>
    /// Checks if the token starts a type (primitive or class name).
    /// </summary>
    public bool IsTypeStart() => IsPrimitiveType() || Type == TokenType.Identifier;

    /// <summary>
    /// Checks if the token is an access modifier.
    /// </summary>
    public bool IsAccessModifier() => Type is TokenType.Public or TokenType.Private
        or TokenType.Protected;
}
