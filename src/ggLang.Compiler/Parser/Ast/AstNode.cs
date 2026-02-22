namespace ggLang.Compiler.Parser.Ast;

/// <summary>
/// Base node of the ggLang Abstract Syntax Tree (AST).
/// </summary>
public abstract class AstNode
{
    public int Line { get; set; }
    public int Column { get; set; }

    /// <summary>
    /// Accepts a visitor to traverse the tree.
    /// </summary>
    public abstract T Accept<T>(IAstVisitor<T> visitor);
}

/// <summary>
/// Visitor pattern for AST traversal.
/// </summary>
public interface IAstVisitor<out T>
{
    // Declarations
    T VisitCompilationUnit(CompilationUnit node);
    T VisitModuleDeclaration(ModuleDeclaration node);
    T VisitImportDeclaration(ImportDeclaration node);
    T VisitClassDeclaration(ClassDeclaration node);
    T VisitInterfaceDeclaration(InterfaceDeclaration node);
    T VisitMethodDeclaration(MethodDeclaration node);
    T VisitConstructorDeclaration(ConstructorDeclaration node);
    T VisitFieldDeclaration(FieldDeclaration node);
    T VisitParameter(ParameterNode node);
    T VisitEnumDeclaration(EnumDeclaration node);
    T VisitAnnotation(AnnotationNode node);

    // Statements
    T VisitBlock(BlockStatement node);
    T VisitVariableDeclaration(VariableDeclarationStatement node);
    T VisitExpressionStatement(ExpressionStatement node);
    T VisitIfStatement(IfStatement node);
    T VisitWhileStatement(WhileStatement node);
    T VisitForStatement(ForStatement node);
    T VisitForEachStatement(ForEachStatement node);
    T VisitReturnStatement(ReturnStatement node);
    T VisitBreakStatement(BreakStatement node);
    T VisitContinueStatement(ContinueStatement node);

    // Expressions
    T VisitBinaryExpression(BinaryExpression node);
    T VisitUnaryExpression(UnaryExpression node);
    T VisitLiteralExpression(LiteralExpression node);
    T VisitIdentifierExpression(IdentifierExpression node);
    T VisitAssignmentExpression(AssignmentExpression node);
    T VisitMethodCallExpression(MethodCallExpression node);
    T VisitMemberAccessExpression(MemberAccessExpression node);
    T VisitObjectCreationExpression(ObjectCreationExpression node);
    T VisitThisExpression(ThisExpression node);
    T VisitBaseExpression(BaseExpression node);
    T VisitCastExpression(CastExpression node);
    T VisitArrayCreationExpression(ArrayCreationExpression node);
    T VisitArrayAccessExpression(ArrayAccessExpression node);
    T VisitNullLiteralExpression(NullLiteralExpression node);
    T VisitTypeReference(TypeReference node);
    T VisitPostfixExpression(PostfixExpression node);
}

// ====================
// DECLARATIONS
// ====================

/// <summary>
/// AST root — represents a complete source file.
/// </summary>
public sealed class CompilationUnit : AstNode
{
    public ModuleDeclaration? Module { get; set; }
    public List<ImportDeclaration> Imports { get; set; } = [];
    public List<AstNode> TypeDeclarations { get; set; } = [];

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitCompilationUnit(this);
}

/// <summary>
/// Module declaration: module Name;
/// </summary>
public sealed class ModuleDeclaration : AstNode
{
    public string Name { get; set; } = "";

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitModuleDeclaration(this);
}

/// <summary>
/// Import declaration: import Name;
/// </summary>
public sealed class ImportDeclaration : AstNode
{
    public string ModuleName { get; set; } = "";

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitImportDeclaration(this);
}

/// <summary>
/// Member access modifiers.
/// </summary>
public enum AccessModifier
{
    Public,
    Private,
    Protected
}

/// <summary>
/// Class declaration.
/// </summary>
public sealed class ClassDeclaration : AstNode
{
    public string Name { get; set; } = "";
    public AccessModifier Access { get; set; } = AccessModifier.Public;
    public bool IsAbstract { get; set; }
    public bool IsSealed { get; set; }
    public bool IsStatic { get; set; }
    public string? BaseClass { get; set; }
    public List<string> Interfaces { get; set; } = [];
    public List<AstNode> Members { get; set; } = [];
    public List<AnnotationNode> Annotations { get; set; } = [];

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitClassDeclaration(this);
}

/// <summary>
/// Interface declaration.
/// </summary>
public sealed class InterfaceDeclaration : AstNode
{
    public string Name { get; set; } = "";
    public AccessModifier Access { get; set; } = AccessModifier.Public;
    public List<string> BaseInterfaces { get; set; } = [];
    public List<MethodDeclaration> Methods { get; set; } = [];

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitInterfaceDeclaration(this);
}

/// <summary>
/// Enum declaration.
/// </summary>
public sealed class EnumDeclaration : AstNode
{
    public string Name { get; set; } = "";
    public AccessModifier Access { get; set; } = AccessModifier.Public;
    public List<(string Name, int? Value)> Members { get; set; } = [];

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitEnumDeclaration(this);
}

/// <summary>
/// Method declaration: [modifiers] ReturnType name(params) { body }
/// C#-style: no 'func' keyword, type comes before name.
/// </summary>
public sealed class MethodDeclaration : AstNode
{
    public string Name { get; set; } = "";
    public AccessModifier Access { get; set; } = AccessModifier.Public;
    public bool IsStatic { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsOverride { get; set; }
    public bool IsAbstract { get; set; }
    public TypeReference ReturnType { get; set; } = null!;
    public List<ParameterNode> Parameters { get; set; } = [];
    public BlockStatement? Body { get; set; } // null for abstract/interface methods
    public List<AnnotationNode> Annotations { get; set; } = [];

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitMethodDeclaration(this);
}

/// <summary>
/// Annotation: [@Name("arg1", "arg2")]
/// Used for metadata like @Library, @Deprecated, @Test.
/// </summary>
public sealed class AnnotationNode : AstNode
{
    public string Name { get; set; } = "";
    public List<Expression> Arguments { get; set; } = [];

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitAnnotation(this);
}

/// <summary>
/// Constructor declaration: ClassName(params) [: base(args)] { body }
/// </summary>
public sealed class ConstructorDeclaration : AstNode
{
    public AccessModifier Access { get; set; } = AccessModifier.Public;
    public List<ParameterNode> Parameters { get; set; } = [];
    public BlockStatement Body { get; set; } = null!;
    public List<Expression>? BaseArguments { get; set; } // arguments for base()

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitConstructorDeclaration(this);
}

/// <summary>
/// Field declaration: [modifiers] Type name [= initializer];
/// C#-style: type before name.
/// </summary>
public sealed class FieldDeclaration : AstNode
{
    public string Name { get; set; } = "";
    public AccessModifier Access { get; set; } = AccessModifier.Private;
    public bool IsStatic { get; set; }
    public bool IsReadOnly { get; set; } // readonly
    public TypeReference Type { get; set; } = null!;
    public Expression? Initializer { get; set; }

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitFieldDeclaration(this);
}

/// <summary>
/// Method/constructor parameter: Type name [= default]
/// </summary>
public sealed class ParameterNode : AstNode
{
    public string Name { get; set; } = "";
    public TypeReference Type { get; set; } = null!;
    public Expression? DefaultValue { get; set; }

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitParameter(this);
}

/// <summary>
/// Type reference with support for arrays, nullables, and generics.
/// </summary>
public sealed class TypeReference : AstNode
{
    public string Name { get; set; } = "";
    public bool IsArray { get; set; }
    public bool IsNullable { get; set; }
    public List<TypeReference> GenericArguments { get; set; } = [];

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitTypeReference(this);

    public override string ToString()
    {
        var name = Name;
        if (GenericArguments.Count > 0)
            name += $"<{string.Join(", ", GenericArguments)}>";
        if (IsArray) name += "[]";
        if (IsNullable) name += "?";
        return name;
    }
}

// ====================
// STATEMENTS
// ====================

public abstract class Statement : AstNode { }

/// <summary>
/// Block statement enclosed in { }.
/// </summary>
public sealed class BlockStatement : Statement
{
    public List<Statement> Statements { get; set; } = [];

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitBlock(this);
}

/// <summary>
/// Local variable declaration: Type name = value; or var name = value;
/// </summary>
public sealed class VariableDeclarationStatement : Statement
{
    public string Name { get; set; } = "";
    public TypeReference? Type { get; set; } // null = type inference (var)
    public Expression? Initializer { get; set; }
    public bool IsReadOnly { get; set; }

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitVariableDeclaration(this);
}

/// <summary>
/// Expression used as a statement.
/// </summary>
public sealed class ExpressionStatement : Statement
{
    public Expression Expression { get; set; } = null!;

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitExpressionStatement(this);
}

/// <summary>
/// If/else statement.
/// </summary>
public sealed class IfStatement : Statement
{
    public Expression Condition { get; set; } = null!;
    public Statement ThenBranch { get; set; } = null!;
    public Statement? ElseBranch { get; set; }

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitIfStatement(this);
}

/// <summary>
/// While loop.
/// </summary>
public sealed class WhileStatement : Statement
{
    public Expression Condition { get; set; } = null!;
    public Statement Body { get; set; } = null!;

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitWhileStatement(this);
}

/// <summary>
/// For loop.
/// </summary>
public sealed class ForStatement : Statement
{
    public Statement? Initializer { get; set; }
    public Expression? Condition { get; set; }
    public Expression? Increment { get; set; }
    public Statement Body { get; set; } = null!;

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitForStatement(this);
}

/// <summary>
/// ForEach loop: foreach (Type name in collection)
/// </summary>
public sealed class ForEachStatement : Statement
{
    public string VariableName { get; set; } = "";
    public TypeReference? VariableType { get; set; }
    public Expression Collection { get; set; } = null!;
    public Statement Body { get; set; } = null!;

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitForEachStatement(this);
}

/// <summary>
/// Return statement.
/// </summary>
public sealed class ReturnStatement : Statement
{
    public Expression? Value { get; set; }

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitReturnStatement(this);
}

public sealed class BreakStatement : Statement
{
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitBreakStatement(this);
}

public sealed class ContinueStatement : Statement
{
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitContinueStatement(this);
}

// ====================
// EXPRESSIONS
// ====================

public abstract class Expression : AstNode
{
    /// <summary>
    /// Type resolved during semantic analysis.
    /// </summary>
    public string? ResolvedType { get; set; }
}

public enum LiteralType
{
    Integer,
    Float,
    String,
    Char,
    Boolean
}

public sealed class LiteralExpression : Expression
{
    public object Value { get; set; } = 0;
    public LiteralType LiteralType { get; set; }

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitLiteralExpression(this);
}

public sealed class NullLiteralExpression : Expression
{
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitNullLiteralExpression(this);
}

public sealed class IdentifierExpression : Expression
{
    public string Name { get; set; } = "";

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitIdentifierExpression(this);
}

public sealed class BinaryExpression : Expression
{
    public Expression Left { get; set; } = null!;
    public string Operator { get; set; } = "";
    public Expression Right { get; set; } = null!;

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitBinaryExpression(this);
}

public sealed class UnaryExpression : Expression
{
    public string Operator { get; set; } = "";
    public Expression Operand { get; set; } = null!;

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitUnaryExpression(this);
}

public sealed class PostfixExpression : Expression
{
    public Expression Operand { get; set; } = null!;
    public string Operator { get; set; } = "";

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitPostfixExpression(this);
}

public sealed class AssignmentExpression : Expression
{
    public Expression Target { get; set; } = null!;
    public string Operator { get; set; } = "=";
    public Expression Value { get; set; } = null!;

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitAssignmentExpression(this);
}

public sealed class MethodCallExpression : Expression
{
    public Expression Target { get; set; } = null!;
    public List<Expression> Arguments { get; set; } = [];

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitMethodCallExpression(this);
}

public sealed class MemberAccessExpression : Expression
{
    public Expression Target { get; set; } = null!;
    public string MemberName { get; set; } = "";

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitMemberAccessExpression(this);
}

public sealed class ObjectCreationExpression : Expression
{
    public string TypeName { get; set; } = "";
    public List<Expression> Arguments { get; set; } = [];

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitObjectCreationExpression(this);
}

public sealed class ThisExpression : Expression
{
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitThisExpression(this);
}

public sealed class BaseExpression : Expression
{
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitBaseExpression(this);
}

public sealed class CastExpression : Expression
{
    public TypeReference TargetType { get; set; } = null!;
    public Expression Expression { get; set; } = null!;

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitCastExpression(this);
}

public sealed class ArrayCreationExpression : Expression
{
    public TypeReference ElementType { get; set; } = null!;
    public Expression Size { get; set; } = null!;

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitArrayCreationExpression(this);
}

public sealed class ArrayAccessExpression : Expression
{
    public Expression Target { get; set; } = null!;
    public Expression Index { get; set; } = null!;

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitArrayAccessExpression(this);
}
