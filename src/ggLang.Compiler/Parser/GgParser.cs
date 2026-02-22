using ggLang.Compiler.Lexer;
using ggLang.Compiler.Parser.Ast;

namespace ggLang.Compiler.Parser;

/// <summary>
/// Recursive descent parser for the ggLang language.
/// Converts a list of tokens into an AST.
///
/// ggLang uses C#-style syntax:
///   - Type before name: int x = 5;
///   - Methods: ReturnType name(params) { body }
///   - Constructors: ClassName(params) { body }
///   - Fields: Type name;
///   - Parameters: Type name
/// </summary>
public sealed class GgParser
{
    private readonly List<Token> _tokens;
    private int _position;
    private readonly List<string> _errors = [];
    private string? _currentClassName;

    public IReadOnlyList<string> Errors => _errors;
    public bool HasErrors => _errors.Count > 0;

    public GgParser(List<Token> tokens)
    {
        _tokens = tokens;
        _position = 0;
    }

    // ====================
    // ENTRY POINT
    // ====================

    /// <summary>
    /// Parses a complete source file.
    /// </summary>
    public CompilationUnit ParseCompilationUnit()
    {
        var unit = new CompilationUnit { Line = Current().Line, Column = Current().Column };

        // module declaration (optional)
        if (Check(TokenType.Module))
        {
            unit.Module = ParseModuleDeclaration();
        }

        // import declarations
        while (Check(TokenType.Import))
        {
            unit.Imports.Add(ParseImportDeclaration());
        }

        // type declarations (classes, interfaces, enums)
        while (!Check(TokenType.EndOfFile))
        {
            var typeDecl = ParseTypeDeclaration();
            if (typeDecl != null)
                unit.TypeDeclarations.Add(typeDecl);
            else
                Advance(); // recover from error
        }

        return unit;
    }

    // ====================
    // TOP-LEVEL DECLARATIONS
    // ====================

    private ModuleDeclaration ParseModuleDeclaration()
    {
        var token = Expect(TokenType.Module);
        var nameToken = Expect(TokenType.Identifier);
        Expect(TokenType.Semicolon);

        return new ModuleDeclaration
        {
            Name = nameToken.Value,
            Line = token.Line,
            Column = token.Column
        };
    }

    private ImportDeclaration ParseImportDeclaration()
    {
        var token = Expect(TokenType.Import);

        // Supports dotted names: import System.IO;
        var name = Expect(TokenType.Identifier).Value;
        while (Check(TokenType.Dot))
        {
            Advance();
            name += "." + Expect(TokenType.Identifier).Value;
        }

        Expect(TokenType.Semicolon);

        return new ImportDeclaration
        {
            ModuleName = name,
            Line = token.Line,
            Column = token.Column
        };
    }

    private AstNode? ParseTypeDeclaration()
    {
        // Parse annotations: [@Name(args)]
        var annotations = ParseAnnotations();

        var access = ParseAccessModifier();
        var modifiers = ParseModifiers();

        if (Check(TokenType.Class))
        {
            var classDecl = ParseClassDeclaration(access, modifiers);
            classDecl.Annotations = annotations;
            return classDecl;
        }
        if (Check(TokenType.Interface))
            return ParseInterfaceDeclaration(access);
        if (Check(TokenType.Enum))
            return ParseEnumDeclaration(access);

        Error($"type declaration expected (class, interface, or enum), but found '{Current().Value}'. All code in ggLang must be inside a type declaration.");
        return null;
    }

    // ====================
    // CLASS DECLARATION
    // ====================

    private ClassDeclaration ParseClassDeclaration(AccessModifier access, HashSet<string> modifiers)
    {
        var token = Expect(TokenType.Class);
        var name = Expect(TokenType.Identifier).Value;

        var classDecl = new ClassDeclaration
        {
            Name = name,
            Access = access,
            IsAbstract = modifiers.Contains("abstract"),
            IsSealed = modifiers.Contains("sealed"),
            IsStatic = modifiers.Contains("static"),
            Line = token.Line,
            Column = token.Column
        };

        // Inheritance: class Foo : Bar, IFoo, IBar
        if (Check(TokenType.Colon))
        {
            Advance();
            var baseName = Expect(TokenType.Identifier).Value;
            classDecl.BaseClass = baseName;

            while (Check(TokenType.Comma))
            {
                Advance();
                classDecl.Interfaces.Add(Expect(TokenType.Identifier).Value);
            }
        }

        // Body
        Expect(TokenType.LeftBrace);
        _currentClassName = name;

        while (!Check(TokenType.RightBrace) && !Check(TokenType.EndOfFile))
        {
            var member = ParseClassMember();
            if (member != null)
                classDecl.Members.Add(member);
        }
        Expect(TokenType.RightBrace);
        _currentClassName = null;

        return classDecl;
    }

    /// <summary>
    /// Parses a class member. C#-style detection:
    ///   - Constructor: ClassName(params) { body }
    ///   - Method: [modifiers] ReturnType name(params) { body }
    ///   - Field: [modifiers] Type name [= value];
    ///
    /// Disambiguation: after parsing type + name, if '(' follows → method, else → field.
    /// If the type name matches the class name and '(' follows → constructor.
    /// </summary>
    private AstNode? ParseClassMember()
    {
        // Parse annotations: [@Name(args)]
        var annotations = ParseAnnotations();

        var memberAccess = ParseAccessModifier();
        var modifiers = ParseModifiers();

        // Check for constructor: ClassName(params)
        if (Check(TokenType.Identifier) && Current().Value == _currentClassName && Peek().Type == TokenType.LeftParen)
        {
            return ParseConstructorDeclaration(memberAccess);
        }

        // Must be a method or field: Type name...
        if (IsTypeStart())
        {
            var member = ParseMethodOrFieldDeclaration(memberAccess, modifiers);
            if (member is MethodDeclaration method)
                method.Annotations = annotations;
            return member;
        }

        Error($"class member expected, but found '{Current().Value}'. Expected a field, method, or constructor declaration. Methods use 'ReturnType name(params)' syntax.");
        Advance();
        return null;
    }

    /// <summary>
    /// Parses either a method or field declaration.
    /// Both start with: Type name
    /// If '(' follows the name → method.
    /// Otherwise → field.
    /// </summary>
    private AstNode ParseMethodOrFieldDeclaration(AccessModifier access, HashSet<string> modifiers)
    {
        var startToken = Current();
        var type = ParseTypeReference();
        var name = Expect(TokenType.Identifier).Value;

        // Method: Type name(params) { body }
        if (Check(TokenType.LeftParen))
        {
            return ParseMethodBody(access, modifiers, type, name, startToken);
        }

        // Field: Type name [= value];
        return ParseFieldBody(access, modifiers, type, name, startToken);
    }

    private MethodDeclaration ParseMethodBody(AccessModifier access, HashSet<string> modifiers,
        TypeReference returnType, string name, Token startToken)
    {
        var method = new MethodDeclaration
        {
            Name = name,
            Access = access,
            IsStatic = modifiers.Contains("static"),
            IsVirtual = modifiers.Contains("virtual"),
            IsOverride = modifiers.Contains("override"),
            IsAbstract = modifiers.Contains("abstract"),
            ReturnType = returnType,
            Line = startToken.Line,
            Column = startToken.Column
        };

        // Parameters
        Expect(TokenType.LeftParen);
        method.Parameters = ParseParameterList();
        Expect(TokenType.RightParen);

        // Body (or ; for abstract)
        if (method.IsAbstract || Check(TokenType.Semicolon))
        {
            if (Check(TokenType.Semicolon)) Advance();
        }
        else
        {
            method.Body = ParseBlock();
        }

        return method;
    }

    private FieldDeclaration ParseFieldBody(AccessModifier access, HashSet<string> modifiers,
        TypeReference type, string name, Token startToken)
    {
        var field = new FieldDeclaration
        {
            Name = name,
            Access = access,
            IsStatic = modifiers.Contains("static"),
            IsReadOnly = modifiers.Contains("readonly"),
            Type = type,
            Line = startToken.Line,
            Column = startToken.Column
        };

        // Optional initializer
        if (Check(TokenType.Equals))
        {
            Advance();
            field.Initializer = ParseExpression();
        }

        Expect(TokenType.Semicolon);
        return field;
    }

    // ====================
    // INTERFACE DECLARATION
    // ====================

    private InterfaceDeclaration ParseInterfaceDeclaration(AccessModifier access)
    {
        var token = Expect(TokenType.Interface);
        var name = Expect(TokenType.Identifier).Value;

        var interfaceDecl = new InterfaceDeclaration
        {
            Name = name,
            Access = access,
            Line = token.Line,
            Column = token.Column
        };

        // Interface inheritance
        if (Check(TokenType.Colon))
        {
            Advance();
            interfaceDecl.BaseInterfaces.Add(Expect(TokenType.Identifier).Value);
            while (Check(TokenType.Comma))
            {
                Advance();
                interfaceDecl.BaseInterfaces.Add(Expect(TokenType.Identifier).Value);
            }
        }

        Expect(TokenType.LeftBrace);
        while (!Check(TokenType.RightBrace) && !Check(TokenType.EndOfFile))
        {
            var method = ParseInterfaceMethod();
            if (method != null)
                interfaceDecl.Methods.Add(method);
        }
        Expect(TokenType.RightBrace);

        return interfaceDecl;
    }

    /// <summary>
    /// Parses an interface method: ReturnType name(params);
    /// </summary>
    private MethodDeclaration? ParseInterfaceMethod()
    {
        if (!IsTypeStart())
        {
            Error($"interface method expected, but found '{Current().Value}'. Interface members must be method signatures: 'ReturnType name(params);'");
            Advance();
            return null;
        }

        var startToken = Current();
        var returnType = ParseTypeReference();
        var name = Expect(TokenType.Identifier).Value;

        var method = new MethodDeclaration
        {
            Name = name,
            Access = AccessModifier.Public,
            IsAbstract = true,
            ReturnType = returnType,
            Line = startToken.Line,
            Column = startToken.Column
        };

        // Parameters
        Expect(TokenType.LeftParen);
        method.Parameters = ParseParameterList();
        Expect(TokenType.RightParen);

        Expect(TokenType.Semicolon);
        return method;
    }

    // ====================
    // ENUM DECLARATION
    // ====================

    private EnumDeclaration ParseEnumDeclaration(AccessModifier access)
    {
        var token = Expect(TokenType.Enum);
        var name = Expect(TokenType.Identifier).Value;

        var enumDecl = new EnumDeclaration
        {
            Name = name,
            Access = access,
            Line = token.Line,
            Column = token.Column
        };

        Expect(TokenType.LeftBrace);
        while (!Check(TokenType.RightBrace) && !Check(TokenType.EndOfFile))
        {
            var memberName = Expect(TokenType.Identifier).Value;
            int? value = null;

            if (Check(TokenType.Equals))
            {
                Advance();
                value = int.Parse(Expect(TokenType.IntegerLiteral).Value);
            }

            enumDecl.Members.Add((memberName, value));

            if (!Check(TokenType.RightBrace))
                Expect(TokenType.Comma);
        }
        Expect(TokenType.RightBrace);

        return enumDecl;
    }

    // ====================
    // CONSTRUCTOR
    // ====================

    private ConstructorDeclaration ParseConstructorDeclaration(AccessModifier access)
    {
        var token = Expect(TokenType.Identifier); // class name

        var ctor = new ConstructorDeclaration
        {
            Access = access,
            Line = token.Line,
            Column = token.Column
        };

        Expect(TokenType.LeftParen);
        ctor.Parameters = ParseParameterList();
        Expect(TokenType.RightParen);

        // Base call: : base(args)
        if (Check(TokenType.Colon))
        {
            Advance();
            Expect(TokenType.Base);
            Expect(TokenType.LeftParen);
            ctor.BaseArguments = ParseArgumentList();
            Expect(TokenType.RightParen);
        }

        ctor.Body = ParseBlock();
        return ctor;
    }

    // ====================
    // PARAMETERS (C#-style: Type name)
    // ====================

    /// <summary>
    /// Parses parameter list: (Type name, Type name, ...)
    /// </summary>
    private List<ParameterNode> ParseParameterList()
    {
        var parameters = new List<ParameterNode>();

        if (Check(TokenType.RightParen))
            return parameters;

        do
        {
            if (Check(TokenType.Comma)) Advance();

            var paramType = ParseTypeReference();
            var paramName = Expect(TokenType.Identifier);

            var param = new ParameterNode
            {
                Name = paramName.Value,
                Type = paramType,
                Line = paramName.Line,
                Column = paramName.Column
            };

            // Default value
            if (Check(TokenType.Equals))
            {
                Advance();
                param.DefaultValue = ParseExpression();
            }

            parameters.Add(param);
        } while (Check(TokenType.Comma));

        return parameters;
    }

    // ====================
    // TYPE REFERENCE
    // ====================

    private TypeReference ParseTypeReference()
    {
        var token = Current();
        string typeName;

        if (token.IsPrimitiveType())
        {
            typeName = token.Value;
            Advance();
        }
        else if (Check(TokenType.Identifier))
        {
            typeName = token.Value;
            Advance();
            // Dotted names: Module.Type
            while (Check(TokenType.Dot))
            {
                Advance();
                typeName += "." + Expect(TokenType.Identifier).Value;
            }
        }
        else
        {
            Error($"type name expected, but found '{token.Value}'. Expected a type like 'int', 'string', 'bool', or a class name.");
            typeName = "???";
        }

        var typeRef = new TypeReference
        {
            Name = typeName,
            Line = token.Line,
            Column = token.Column
        };

        // Array: Type[]
        if (Check(TokenType.LeftBracket))
        {
            Advance();
            Expect(TokenType.RightBracket);
            typeRef.IsArray = true;
        }

        // Nullable: Type?
        if (Check(TokenType.QuestionMark))
        {
            Advance();
            typeRef.IsNullable = true;
        }

        return typeRef;
    }

    // ====================
    // STATEMENTS
    // ====================

    private BlockStatement ParseBlock()
    {
        var token = Expect(TokenType.LeftBrace);
        var block = new BlockStatement { Line = token.Line, Column = token.Column };

        while (!Check(TokenType.RightBrace) && !Check(TokenType.EndOfFile))
        {
            var stmt = ParseStatement();
            if (stmt != null)
                block.Statements.Add(stmt);
        }

        Expect(TokenType.RightBrace);
        return block;
    }

    private Statement? ParseStatement()
    {
        // Block
        if (Check(TokenType.LeftBrace))
            return ParseBlock();

        // Variable declaration with 'var' keyword (type inference)
        if (Check(TokenType.Var))
            return ParseVarDeclaration();

        // If
        if (Check(TokenType.If))
            return ParseIfStatement();

        // While
        if (Check(TokenType.While))
            return ParseWhileStatement();

        // For
        if (Check(TokenType.For))
            return ParseForStatement();

        // ForEach
        if (Check(TokenType.ForEach))
            return ParseForEachStatement();

        // Return
        if (Check(TokenType.Return))
            return ParseReturnStatement();

        // Break
        if (Check(TokenType.Break))
        {
            var token = Advance();
            Expect(TokenType.Semicolon);
            return new BreakStatement { Line = token.Line, Column = token.Column };
        }

        // Continue
        if (Check(TokenType.Continue))
        {
            var token = Advance();
            Expect(TokenType.Semicolon);
            return new ContinueStatement { Line = token.Line, Column = token.Column };
        }

        // Check for typed variable declaration: Type name = value;
        // Disambiguate from expression statement using lookahead
        if (IsVarDeclStart())
            return ParseTypedVariableDeclaration();

        // Expression statement
        return ParseExpressionStatement();
    }

    /// <summary>
    /// Checks if the current position starts a typed variable declaration.
    /// Pattern: PrimitiveType Identifier or ClassName Identifier (not followed by '.')
    /// Must distinguish from expression statements like: Console.writeLine(...)
    /// </summary>
    private bool IsVarDeclStart()
    {
        // Primitive type followed by identifier: int x = ...
        if (Current().IsPrimitiveType() && Peek().Type == TokenType.Identifier)
        {
            return true;
        }

        // Class type: Identifier followed by identifier (not a method call or member access)
        if (Check(TokenType.Identifier))
        {
            var next = Peek();
            // ClassName varName → variable declaration
            if (next.Type == TokenType.Identifier)
                return true;
            // ClassName[] varName → array declaration
            if (next.Type == TokenType.LeftBracket && Peek(2).Type == TokenType.RightBracket)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Parses: var name = value;
    /// </summary>
    private VariableDeclarationStatement ParseVarDeclaration()
    {
        var token = Expect(TokenType.Var);
        var name = Expect(TokenType.Identifier).Value;

        Expression? initializer = null;
        if (Check(TokenType.Equals))
        {
            Advance();
            initializer = ParseExpression();
        }

        Expect(TokenType.Semicolon);

        return new VariableDeclarationStatement
        {
            Name = name,
            Type = null, // type inference
            Initializer = initializer,
            IsReadOnly = false,
            Line = token.Line,
            Column = token.Column
        };
    }

    /// <summary>
    /// Parses: Type name [= value];
    /// </summary>
    private VariableDeclarationStatement ParseTypedVariableDeclaration()
    {
        var startToken = Current();
        var type = ParseTypeReference();
        var name = Expect(TokenType.Identifier).Value;

        Expression? initializer = null;
        if (Check(TokenType.Equals))
        {
            Advance();
            initializer = ParseExpression();
        }

        Expect(TokenType.Semicolon);

        return new VariableDeclarationStatement
        {
            Name = name,
            Type = type,
            Initializer = initializer,
            IsReadOnly = false,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    private IfStatement ParseIfStatement()
    {
        var token = Expect(TokenType.If);
        Expect(TokenType.LeftParen);
        var condition = ParseExpression();
        Expect(TokenType.RightParen);

        var thenBranch = Check(TokenType.LeftBrace)
            ? (Statement)ParseBlock()
            : ParseStatement()!;

        Statement? elseBranch = null;
        if (Check(TokenType.Else))
        {
            Advance();
            elseBranch = Check(TokenType.LeftBrace)
                ? ParseBlock()
                : ParseStatement();
        }

        return new IfStatement
        {
            Condition = condition,
            ThenBranch = thenBranch,
            ElseBranch = elseBranch,
            Line = token.Line,
            Column = token.Column
        };
    }

    private WhileStatement ParseWhileStatement()
    {
        var token = Expect(TokenType.While);
        Expect(TokenType.LeftParen);
        var condition = ParseExpression();
        Expect(TokenType.RightParen);

        var body = Check(TokenType.LeftBrace)
            ? (Statement)ParseBlock()
            : ParseStatement()!;

        return new WhileStatement
        {
            Condition = condition,
            Body = body,
            Line = token.Line,
            Column = token.Column
        };
    }

    private ForStatement ParseForStatement()
    {
        var token = Expect(TokenType.For);
        Expect(TokenType.LeftParen);

        // Initializer
        Statement? init = null;
        if (Check(TokenType.Var))
        {
            init = ParseVarDeclaration();
        }
        else if (IsVarDeclStart())
        {
            init = ParseTypedVariableDeclaration();
        }
        else if (!Check(TokenType.Semicolon))
        {
            init = ParseExpressionStatement();
        }
        else
        {
            Advance(); // ;
        }

        // Condition
        Expression? condition = null;
        if (!Check(TokenType.Semicolon))
        {
            condition = ParseExpression();
        }
        Expect(TokenType.Semicolon);

        // Increment
        Expression? increment = null;
        if (!Check(TokenType.RightParen))
        {
            increment = ParseExpression();
        }
        Expect(TokenType.RightParen);

        var body = Check(TokenType.LeftBrace)
            ? (Statement)ParseBlock()
            : ParseStatement()!;

        return new ForStatement
        {
            Initializer = init,
            Condition = condition,
            Increment = increment,
            Body = body,
            Line = token.Line,
            Column = token.Column
        };
    }

    private ForEachStatement ParseForEachStatement()
    {
        var token = Expect(TokenType.ForEach);
        Expect(TokenType.LeftParen);

        // foreach (Type name in collection)
        TypeReference? varType = null;
        string varName;

        if (IsTypeStart() && Peek().Type == TokenType.Identifier)
        {
            varType = ParseTypeReference();
            varName = Expect(TokenType.Identifier).Value;
        }
        else
        {
            varName = Expect(TokenType.Identifier).Value;
        }

        Expect(TokenType.In);
        var collection = ParseExpression();
        Expect(TokenType.RightParen);

        var body = Check(TokenType.LeftBrace)
            ? (Statement)ParseBlock()
            : ParseStatement()!;

        return new ForEachStatement
        {
            VariableName = varName,
            VariableType = varType,
            Collection = collection,
            Body = body,
            Line = token.Line,
            Column = token.Column
        };
    }

    private ReturnStatement ParseReturnStatement()
    {
        var token = Expect(TokenType.Return);
        Expression? value = null;

        if (!Check(TokenType.Semicolon))
        {
            value = ParseExpression();
        }

        Expect(TokenType.Semicolon);

        return new ReturnStatement
        {
            Value = value,
            Line = token.Line,
            Column = token.Column
        };
    }

    private ExpressionStatement ParseExpressionStatement()
    {
        var expr = ParseExpression();
        Expect(TokenType.Semicolon);

        return new ExpressionStatement
        {
            Expression = expr,
            Line = expr.Line,
            Column = expr.Column
        };
    }

    // ====================
    // EXPRESSIONS (Pratt parser / precedence climbing)
    // ====================

    private Expression ParseExpression()
    {
        return ParseAssignment();
    }

    private Expression ParseAssignment()
    {
        var expr = ParseOr();

        if (Check(TokenType.Equals) || Check(TokenType.PlusEquals) ||
            Check(TokenType.MinusEquals) || Check(TokenType.StarEquals) ||
            Check(TokenType.SlashEquals))
        {
            var op = Advance().Value;
            var value = ParseAssignment(); // right-associative

            return new AssignmentExpression
            {
                Target = expr,
                Operator = op,
                Value = value,
                Line = expr.Line,
                Column = expr.Column
            };
        }

        return expr;
    }

    private Expression ParseOr()
    {
        var left = ParseAnd();

        while (Check(TokenType.PipePipe))
        {
            var op = Advance().Value;
            var right = ParseAnd();
            left = new BinaryExpression
            {
                Left = left, Operator = op, Right = right,
                Line = left.Line, Column = left.Column
            };
        }

        return left;
    }

    private Expression ParseAnd()
    {
        var left = ParseEquality();

        while (Check(TokenType.AmpersandAmpersand))
        {
            var op = Advance().Value;
            var right = ParseEquality();
            left = new BinaryExpression
            {
                Left = left, Operator = op, Right = right,
                Line = left.Line, Column = left.Column
            };
        }

        return left;
    }

    private Expression ParseEquality()
    {
        var left = ParseComparison();

        while (Check(TokenType.EqualsEquals) || Check(TokenType.BangEquals))
        {
            var op = Advance().Value;
            var right = ParseComparison();
            left = new BinaryExpression
            {
                Left = left, Operator = op, Right = right,
                Line = left.Line, Column = left.Column
            };
        }

        return left;
    }

    private Expression ParseComparison()
    {
        var left = ParseBitwise();

        while (Check(TokenType.Less) || Check(TokenType.Greater) ||
               Check(TokenType.LessEquals) || Check(TokenType.GreaterEquals))
        {
            var op = Advance().Value;
            var right = ParseBitwise();
            left = new BinaryExpression
            {
                Left = left, Operator = op, Right = right,
                Line = left.Line, Column = left.Column
            };
        }

        return left;
    }

    private Expression ParseBitwise()
    {
        var left = ParseShift();

        while (Check(TokenType.Ampersand) || Check(TokenType.Pipe) || Check(TokenType.Caret))
        {
            var op = Advance().Value;
            var right = ParseShift();
            left = new BinaryExpression
            {
                Left = left, Operator = op, Right = right,
                Line = left.Line, Column = left.Column
            };
        }

        return left;
    }

    private Expression ParseShift()
    {
        var left = ParseAdditive();

        while (Check(TokenType.LessLess) || Check(TokenType.GreaterGreater))
        {
            var op = Advance().Value;
            var right = ParseAdditive();
            left = new BinaryExpression
            {
                Left = left, Operator = op, Right = right,
                Line = left.Line, Column = left.Column
            };
        }

        return left;
    }

    private Expression ParseAdditive()
    {
        var left = ParseMultiplicative();

        while (Check(TokenType.Plus) || Check(TokenType.Minus))
        {
            var op = Advance().Value;
            var right = ParseMultiplicative();
            left = new BinaryExpression
            {
                Left = left, Operator = op, Right = right,
                Line = left.Line, Column = left.Column
            };
        }

        return left;
    }

    private Expression ParseMultiplicative()
    {
        var left = ParseUnary();

        while (Check(TokenType.Star) || Check(TokenType.Slash) || Check(TokenType.Percent))
        {
            var op = Advance().Value;
            var right = ParseUnary();
            left = new BinaryExpression
            {
                Left = left, Operator = op, Right = right,
                Line = left.Line, Column = left.Column
            };
        }

        return left;
    }

    private Expression ParseUnary()
    {
        if (Check(TokenType.Bang) || Check(TokenType.Minus) || Check(TokenType.Tilde) ||
            Check(TokenType.PlusPlus) || Check(TokenType.MinusMinus))
        {
            var op = Advance();
            var operand = ParseUnary();
            return new UnaryExpression
            {
                Operator = op.Value,
                Operand = operand,
                Line = op.Line,
                Column = op.Column
            };
        }

        return ParsePostfix();
    }

    private Expression ParsePostfix()
    {
        var expr = ParsePrimary();

        while (true)
        {
            // Member access: .member
            if (Check(TokenType.Dot))
            {
                Advance();
                var memberName = Expect(TokenType.Identifier).Value;
                expr = new MemberAccessExpression
                {
                    Target = expr,
                    MemberName = memberName,
                    Line = expr.Line,
                    Column = expr.Column
                };
            }
            // Method call: (args)
            else if (Check(TokenType.LeftParen))
            {
                Advance();
                var args = ParseArgumentList();
                Expect(TokenType.RightParen);
                expr = new MethodCallExpression
                {
                    Target = expr,
                    Arguments = args,
                    Line = expr.Line,
                    Column = expr.Column
                };
            }
            // Array access: [index]
            else if (Check(TokenType.LeftBracket))
            {
                Advance();
                var index = ParseExpression();
                Expect(TokenType.RightBracket);
                expr = new ArrayAccessExpression
                {
                    Target = expr,
                    Index = index,
                    Line = expr.Line,
                    Column = expr.Column
                };
            }
            // Postfix ++ --
            else if (Check(TokenType.PlusPlus) || Check(TokenType.MinusMinus))
            {
                var op = Advance();
                expr = new PostfixExpression
                {
                    Operand = expr,
                    Operator = op.Value,
                    Line = expr.Line,
                    Column = expr.Column
                };
            }
            // Cast: expr as Type
            else if (Check(TokenType.As))
            {
                Advance();
                var targetType = ParseTypeReference();
                expr = new CastExpression
                {
                    Expression = expr,
                    TargetType = targetType,
                    Line = expr.Line,
                    Column = expr.Column
                };
            }
            else
            {
                break;
            }
        }

        return expr;
    }

    private Expression ParsePrimary()
    {
        var token = Current();

        // Integer literal
        if (Check(TokenType.IntegerLiteral))
        {
            Advance();
            return new LiteralExpression
            {
                Value = long.Parse(token.Value.TrimEnd('l', 'L')),
                LiteralType = LiteralType.Integer,
                Line = token.Line,
                Column = token.Column
            };
        }

        // Float literal
        if (Check(TokenType.FloatLiteral))
        {
            Advance();
            var val = token.Value.TrimEnd('f', 'F', 'd', 'D');
            return new LiteralExpression
            {
                Value = double.Parse(val, System.Globalization.CultureInfo.InvariantCulture),
                LiteralType = LiteralType.Float,
                Line = token.Line,
                Column = token.Column
            };
        }

        // String literal
        if (Check(TokenType.StringLiteral))
        {
            Advance();
            return new LiteralExpression
            {
                Value = token.Value,
                LiteralType = LiteralType.String,
                Line = token.Line,
                Column = token.Column
            };
        }

        // Char literal
        if (Check(TokenType.CharLiteral))
        {
            Advance();
            return new LiteralExpression
            {
                Value = token.Value.Length > 0 ? token.Value[0] : '\0',
                LiteralType = LiteralType.Char,
                Line = token.Line,
                Column = token.Column
            };
        }

        // Boolean literals
        if (Check(TokenType.True))
        {
            Advance();
            return new LiteralExpression
            {
                Value = true,
                LiteralType = LiteralType.Boolean,
                Line = token.Line,
                Column = token.Column
            };
        }
        if (Check(TokenType.False))
        {
            Advance();
            return new LiteralExpression
            {
                Value = false,
                LiteralType = LiteralType.Boolean,
                Line = token.Line,
                Column = token.Column
            };
        }

        // Null
        if (Check(TokenType.Null))
        {
            Advance();
            return new NullLiteralExpression { Line = token.Line, Column = token.Column };
        }

        // This
        if (Check(TokenType.This))
        {
            Advance();
            return new ThisExpression { Line = token.Line, Column = token.Column };
        }

        // Base
        if (Check(TokenType.Base))
        {
            Advance();
            return new BaseExpression { Line = token.Line, Column = token.Column };
        }

        // New: new Type(args) or new Type[size]
        if (Check(TokenType.New))
        {
            return ParseNewExpression();
        }

        // Identifier
        if (Check(TokenType.Identifier))
        {
            Advance();
            return new IdentifierExpression
            {
                Name = token.Value,
                Line = token.Line,
                Column = token.Column
            };
        }

        // Parenthesized expression
        if (Check(TokenType.LeftParen))
        {
            Advance();
            var expr = ParseExpression();
            Expect(TokenType.RightParen);
            return expr;
        }

        Error($"expression expected, but found '{token.Value}'. Expected a value, variable, method call, or operator expression.");
        Advance();
        return new LiteralExpression
        {
            Value = 0,
            LiteralType = LiteralType.Integer,
            Line = token.Line,
            Column = token.Column
        };
    }

    private Expression ParseNewExpression()
    {
        var token = Expect(TokenType.New);
        var typeName = Expect(TokenType.Identifier).Value;

        // Dotted names
        while (Check(TokenType.Dot))
        {
            Advance();
            typeName += "." + Expect(TokenType.Identifier).Value;
        }

        // Array: new Type[size]
        if (Check(TokenType.LeftBracket))
        {
            Advance();
            var size = ParseExpression();
            Expect(TokenType.RightBracket);

            return new ArrayCreationExpression
            {
                ElementType = new TypeReference { Name = typeName, Line = token.Line, Column = token.Column },
                Size = size,
                Line = token.Line,
                Column = token.Column
            };
        }

        // Object: new Type(args)
        Expect(TokenType.LeftParen);
        var args = ParseArgumentList();
        Expect(TokenType.RightParen);

        return new ObjectCreationExpression
        {
            TypeName = typeName,
            Arguments = args,
            Line = token.Line,
            Column = token.Column
        };
    }

    private List<Expression> ParseArgumentList()
    {
        var args = new List<Expression>();

        if (Check(TokenType.RightParen))
            return args;

        args.Add(ParseExpression());
        while (Check(TokenType.Comma))
        {
            Advance();
            args.Add(ParseExpression());
        }

        return args;
    }

    // ====================
    // HELPERS
    // ====================

    /// <summary>
    /// Checks if the current token can start a type reference.
    /// </summary>
    private bool IsTypeStart()
    {
        return Current().IsPrimitiveType() || Check(TokenType.Identifier);
    }

    private AccessModifier ParseAccessModifier()
    {
        if (Check(TokenType.Public)) { Advance(); return AccessModifier.Public; }
        if (Check(TokenType.Private)) { Advance(); return AccessModifier.Private; }
        if (Check(TokenType.Protected)) { Advance(); return AccessModifier.Protected; }
        return AccessModifier.Public; // default
    }

    /// <summary>
    /// Parses zero or more annotations: [@Name] or [@Name("arg1", "arg2")]
    /// </summary>
    private List<AnnotationNode> ParseAnnotations()
    {
        var annotations = new List<AnnotationNode>();

        while (Check(TokenType.LeftBracket) && Peek().Type == TokenType.At)
        {
            var token = Advance(); // [
            Advance(); // @
            var name = Expect(TokenType.Identifier).Value;

            var args = new List<Expression>();
            if (Check(TokenType.LeftParen))
            {
                Advance(); // (
                if (!Check(TokenType.RightParen))
                {
                    args.Add(ParseExpression());
                    while (Check(TokenType.Comma))
                    {
                        Advance();
                        args.Add(ParseExpression());
                    }
                }
                Expect(TokenType.RightParen);
            }

            Expect(TokenType.RightBracket);

            annotations.Add(new AnnotationNode
            {
                Name = name,
                Arguments = args,
                Line = token.Line,
                Column = token.Column
            });
        }

        return annotations;
    }

    private HashSet<string> ParseModifiers()
    {
        var modifiers = new HashSet<string>();
        while (true)
        {
            if (Check(TokenType.Static)) { modifiers.Add("static"); Advance(); }
            else if (Check(TokenType.Abstract)) { modifiers.Add("abstract"); Advance(); }
            else if (Check(TokenType.Virtual)) { modifiers.Add("virtual"); Advance(); }
            else if (Check(TokenType.Override)) { modifiers.Add("override"); Advance(); }
            else if (Check(TokenType.Sealed)) { modifiers.Add("sealed"); Advance(); }
            else if (Check(TokenType.Readonly)) { modifiers.Add("readonly"); Advance(); }
            else break;
        }
        return modifiers;
    }

    private Token Current()
    {
        return _position < _tokens.Count
            ? _tokens[_position]
            : _tokens[^1]; // EOF
    }

    private Token Peek(int offset = 1)
    {
        var pos = _position + offset;
        return pos < _tokens.Count
            ? _tokens[pos]
            : _tokens[^1];
    }

    private bool Check(TokenType type)
    {
        return Current().Type == type;
    }

    private Token Advance()
    {
        var current = Current();
        if (_position < _tokens.Count)
            _position++;
        return current;
    }

    private Token Expect(TokenType type)
    {
        if (Check(type))
            return Advance();

        var current = Current();
        Error($"expected '{type}' but found '{current.Value}' ({current.Type}). Check for missing punctuation or mismatched brackets.");
        return current;
    }

    private void Error(string message)
    {
        var token = Current();
        _errors.Add($"{token.Position}: {message}");
    }
}
