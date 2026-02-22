namespace ggLang.Compiler.Lexer;

/// <summary>
/// All token types recognized by the ggLang lexer.
/// </summary>
public enum TokenType
{
    // === Literals ===
    IntegerLiteral,
    FloatLiteral,
    StringLiteral,
    CharLiteral,

    // === Identifier ===
    Identifier,

    // === Keywords ===
    Module,         // module
    Import,         // import
    Class,          // class
    Interface,      // interface
    Struct,         // struct
    Enum,           // enum
    If,             // if
    Else,           // else
    While,          // while
    For,            // for
    ForEach,        // foreach
    In,             // in
    Return,         // return
    Break,          // break
    Continue,       // continue
    New,            // new
    This,           // this
    Base,           // base
    Null,           // null
    True,           // true
    False,          // false
    Static,         // static
    Public,         // public
    Private,        // private
    Protected,      // protected
    Abstract,       // abstract
    Virtual,        // virtual
    Override,       // override
    Sealed,         // sealed
    Readonly,       // readonly
    Const,          // const
    As,             // as
    Is,             // is
    Match,          // match (pattern matching)
    Case,           // case
    Default,        // default
    Var,            // var (type inference)

    // === Primitive types ===
    Int,            // int
    Float,          // float
    Double,         // double
    Bool,           // bool
    Char,           // char
    StringType,     // string
    Void,           // void
    Long,           // long
    Byte,           // byte

    // === Arithmetic operators ===
    Plus,           // +
    Minus,          // -
    Star,           // *
    Slash,          // /
    Percent,        // %

    // === Assignment operators ===
    Equals,         // =
    PlusEquals,     // +=
    MinusEquals,    // -=
    StarEquals,     // *=
    SlashEquals,    // /=

    // === Comparison operators ===
    EqualsEquals,   // ==
    BangEquals,     // !=
    Less,           // <
    Greater,        // >
    LessEquals,     // <=
    GreaterEquals,  // >=

    // === Logical operators ===
    AmpersandAmpersand, // &&
    PipePipe,           // ||
    Bang,               // !

    // === Bitwise operators ===
    Ampersand,      // &
    Pipe,           // |
    Caret,          // ^
    Tilde,          // ~
    LessLess,       // <<
    GreaterGreater, // >>

    // === Delimiters ===
    LeftParen,      // (
    RightParen,     // )
    LeftBrace,      // {
    RightBrace,     // }
    LeftBracket,    // [
    RightBracket,   // ]

    // === Punctuation ===
    Semicolon,      // ;
    Colon,          // :
    Comma,          // ,
    Dot,            // .
    Arrow,          // =>
    QuestionMark,   // ?
    PlusPlus,       // ++
    MinusMinus,     // --

    // === Special ===
    At,             // @ (used in annotations: [@Name])
    EndOfFile,
    Invalid
}
