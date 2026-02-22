using ggLang.Compiler.Parser.Ast;

namespace ggLang.Compiler.Analysis;

/// <summary>
/// Semantic analyzer for ggLang.
/// Performs 3 passes:
///   1) Register all types (classes, interfaces, enums)
///   2) Register all members (fields, methods, constructors) and build inheritance
///   3) Analyze method bodies and expressions
/// </summary>
public sealed class SemanticAnalyzer
{
    private readonly DiagnosticBag _diagnostics = new();
    private readonly Dictionary<string, ClassInfo> _classTable = [];
    private SymbolTable _globalScope = new("global");
    private SymbolTable _currentScope;
    private string? _currentClassName;
    private TypeInfo? _currentReturnType;
    private HashSet<string> _currentParameters = [];

    /// <summary>
    /// Tracks deprecated classes: className → (message, removalVersion)
    /// </summary>
    private readonly Dictionary<string, (string? Message, string? RemovalVersion)> _deprecatedClasses = [];

    /// <summary>
    /// Tracks removed classes: className → (message, version)
    /// </summary>
    private readonly Dictionary<string, (string? Message, string? Version)> _removedClasses = [];

    /// <summary>
    /// Tracks deprecated methods: "className.methodName" → (message, removalVersion)
    /// </summary>
    private readonly Dictionary<string, (string? Message, string? RemovalVersion)> _deprecatedMethods = [];

    /// <summary>
    /// Tracks removed methods: "className.methodName" → (message, version)
    /// </summary>
    private readonly Dictionary<string, (string? Message, string? Version)> _removedMethods = [];

    public DiagnosticBag Diagnostics => _diagnostics;
    public Dictionary<string, ClassInfo> ClassTable => _classTable;
    public bool HasErrors => _diagnostics.HasErrors;

    public SemanticAnalyzer()
    {
        _currentScope = _globalScope;
        _globalScope.RegisterBuiltinTypes();
    }

    // ====================
    // PUBLIC ENTRY
    // ====================

    public void Analyze(CompilationUnit unit)
    {
        // Pass 1: Register all types
        foreach (var typeDecl in unit.TypeDeclarations)
        {
            if (typeDecl is ClassDeclaration classDecl)
                RegisterClass(classDecl);
            else if (typeDecl is InterfaceDeclaration ifaceDecl)
                RegisterInterface(ifaceDecl);
            else if (typeDecl is EnumDeclaration enumDecl)
                RegisterEnum(enumDecl);
        }

        // Pass 2: Register members & resolve inheritance
        foreach (var typeDecl in unit.TypeDeclarations)
        {
            if (typeDecl is ClassDeclaration classDecl)
                RegisterClassMembers(classDecl);
        }

        // Resolve inheritance chains (register inherited members)
        ResolveInheritance();

        // Pass 3: Analyze bodies
        foreach (var typeDecl in unit.TypeDeclarations)
        {
            if (typeDecl is ClassDeclaration classDecl)
                AnalyzeClassBodies(classDecl);
        }
    }

    // ====================
    // PASS 1: REGISTER TYPES
    // ====================

    private void RegisterClass(ClassDeclaration classDecl)
    {
        if (_classTable.ContainsKey(classDecl.Name))
        {
            _diagnostics.ReportError(
                $"duplicate type definition: '{classDecl.Name}' is already declared. " +
                $"Each type name must be unique within the compilation unit. " +
                $"Consider renaming one of the declarations.",
                classDecl.Line, classDecl.Column);
            return;
        }

        var classInfo = new ClassInfo
        {
            Name = classDecl.Name,
            BaseClassName = classDecl.BaseClass,
            IsAbstract = classDecl.IsAbstract,
            IsSealed = classDecl.IsSealed,
            Interfaces = new List<string>(classDecl.Interfaces)
        };
        _classTable[classDecl.Name] = classInfo;

        // Validate annotations on the class
        ValidateAnnotations(classDecl.Annotations, classDecl.Name, null, classDecl.Line, classDecl.Column);

        var symbol = new Symbol
        {
            Name = classDecl.Name,
            Kind = SymbolKind.Class,
            Type = new TypeInfo { Name = classDecl.Name },
            IsPublic = classDecl.Access == AccessModifier.Public,
            Line = classDecl.Line,
            Column = classDecl.Column
        };
        _globalScope.Declare(symbol);
    }

    private void RegisterInterface(InterfaceDeclaration ifaceDecl)
    {
        var symbol = new Symbol
        {
            Name = ifaceDecl.Name,
            Kind = SymbolKind.Interface,
            Type = new TypeInfo { Name = ifaceDecl.Name },
            IsPublic = ifaceDecl.Access == AccessModifier.Public,
            Line = ifaceDecl.Line,
            Column = ifaceDecl.Column
        };
        _globalScope.Declare(symbol);
    }

    private void RegisterEnum(EnumDeclaration enumDecl)
    {
        var symbol = new Symbol
        {
            Name = enumDecl.Name,
            Kind = SymbolKind.Enum,
            Type = new TypeInfo { Name = enumDecl.Name },
            IsPublic = enumDecl.Access == AccessModifier.Public,
            Line = enumDecl.Line,
            Column = enumDecl.Column
        };
        _globalScope.Declare(symbol);
    }

    // ====================
    // PASS 2: REGISTER MEMBERS
    // ====================

    private void RegisterClassMembers(ClassDeclaration classDecl)
    {
        if (!_classTable.TryGetValue(classDecl.Name, out var classInfo))
            return;

        foreach (var member in classDecl.Members)
        {
            switch (member)
            {
                case FieldDeclaration field:
                    var fieldSymbol = new Symbol
                    {
                        Name = field.Name,
                        Kind = SymbolKind.Field,
                        Type = ResolveTypeInfo(field.Type),
                        IsPublic = field.Access == AccessModifier.Public,
                        IsStatic = field.IsStatic,
                        IsReadOnly = field.IsReadOnly,
                        Line = field.Line,
                        Column = field.Column
                    };
                    if (!classInfo.Fields.TryAdd(field.Name, fieldSymbol))
                        _diagnostics.ReportError(
                            $"duplicate field: '{field.Name}' is already declared in class '{classDecl.Name}'. " +
                            $"Field names must be unique within a class, including inherited fields.",
                            field.Line, field.Column);
                    break;

                case MethodDeclaration method:
                    var methodSymbol = new Symbol
                    {
                        Name = method.Name,
                        Kind = SymbolKind.Method,
                        Type = ResolveTypeInfo(method.ReturnType),
                        IsPublic = method.Access == AccessModifier.Public,
                        IsStatic = method.IsStatic,
                        Line = method.Line,
                        Column = method.Column
                    };
                    classInfo.Methods[method.Name] = methodSymbol;
                    break;

                case ConstructorDeclaration:
                    classInfo.HasConstructor = true;
                    break;
            }
        }
    }

    /// <summary>
    /// Resolves inheritance chains: copies base class members to derived classes.
    /// Handles multi-level inheritance (A ← B ← C).
    /// </summary>
    private void ResolveInheritance()
    {
        var resolved = new HashSet<string>();

        void ResolveClass(string className)
        {
            if (resolved.Contains(className)) return;
            if (!_classTable.TryGetValue(className, out var classInfo)) return;

            // Resolve base class first
            if (classInfo.BaseClassName != null)
            {
                if (!_classTable.ContainsKey(classInfo.BaseClassName))
                {
                    _diagnostics.ReportError(
                        $"undefined base class: '{classInfo.BaseClassName}' used as base class for '{className}' " +
                        $"was not found. Ensure the base class is defined in the same compilation unit or " +
                        $"imported correctly. Check for typos in the class name.",
                        0, 0);
                }
                else
                {
                    ResolveClass(classInfo.BaseClassName);
                    var baseClass = _classTable[classInfo.BaseClassName];

                    // Inherit fields
                    foreach (var (name, field) in baseClass.Fields)
                    {
                        classInfo.Fields.TryAdd(name, field);
                    }

                    // Inherit methods (don't override if already declared)
                    foreach (var (name, method) in baseClass.Methods)
                    {
                        classInfo.Methods.TryAdd(name, method);
                    }

                    // Inherit interfaces
                    foreach (var iface in baseClass.Interfaces)
                    {
                        if (!classInfo.Interfaces.Contains(iface))
                            classInfo.Interfaces.Add(iface);
                    }
                }
            }

            resolved.Add(className);
        }

        foreach (var className in _classTable.Keys.ToList())
        {
            ResolveClass(className);
        }
    }

    // ====================
    // PASS 3: ANALYZE BODIES
    // ====================

    private void AnalyzeClassBodies(ClassDeclaration classDecl)
    {
        _currentClassName = classDecl.Name;

        // Create class scope
        var classScope = _globalScope.CreateChild($"class:{classDecl.Name}");
        var prevScope = _currentScope;
        _currentScope = classScope;

        // Register 'this'
        classScope.Declare(new Symbol
        {
            Name = "this",
            Kind = SymbolKind.Variable,
            Type = new TypeInfo { Name = classDecl.Name },
            IsPublic = false
        });

        // Register fields as local symbols
        if (_classTable.TryGetValue(classDecl.Name, out var classInfo))
        {
            foreach (var (name, field) in classInfo.Fields)
            {
                classScope.Declare(field);
            }
        }

        foreach (var member in classDecl.Members)
        {
            switch (member)
            {
                case MethodDeclaration method:
                    AnalyzeMethod(method);
                    break;
                case ConstructorDeclaration ctor:
                    AnalyzeConstructor(ctor);
                    break;
                case FieldDeclaration field:
                    if (field.Initializer != null)
                        AnalyzeExpression(field.Initializer);
                    break;
            }
        }

        _currentScope = prevScope;
        _currentClassName = null;
    }

    private void AnalyzeMethod(MethodDeclaration method)
    {
        // Validate method-level annotations
        if (_currentClassName != null)
            ValidateAnnotations(method.Annotations, _currentClassName, method.Name, method.Line, method.Column);

        var methodScope = _currentScope.CreateChild($"method:{method.Name}");
        var prevScope = _currentScope;
        var prevReturnType = _currentReturnType;
        var prevParams = _currentParameters;

        _currentScope = methodScope;
        _currentReturnType = ResolveTypeInfo(method.ReturnType);
        _currentParameters = [];

        // Register parameters
        foreach (var param in method.Parameters)
        {
            var paramSymbol = new Symbol
            {
                Name = param.Name,
                Kind = SymbolKind.Parameter,
                Type = ResolveTypeInfo(param.Type),
                IsPublic = false,
                Line = param.Line,
                Column = param.Column
            };
            if (!methodScope.Declare(paramSymbol))
            {
                _diagnostics.ReportError(
                    $"duplicate parameter: '{param.Name}' is already declared in this method's parameter list. " +
                    $"Each parameter must have a unique name.",
                    param.Line, param.Column);
            }
            _currentParameters.Add(param.Name);
        }

        // Analyze body
        if (method.Body != null)
        {
            AnalyzeBlock(method.Body);
        }

        _currentScope = prevScope;
        _currentReturnType = prevReturnType;
        _currentParameters = prevParams;
    }

    private void AnalyzeConstructor(ConstructorDeclaration ctor)
    {
        var ctorScope = _currentScope.CreateChild("constructor");
        var prevScope = _currentScope;
        var prevParams = _currentParameters;

        _currentScope = ctorScope;
        _currentParameters = [];

        // Register parameters
        foreach (var param in ctor.Parameters)
        {
            var paramSymbol = new Symbol
            {
                Name = param.Name,
                Kind = SymbolKind.Parameter,
                Type = ResolveTypeInfo(param.Type),
                IsPublic = false,
                Line = param.Line,
                Column = param.Column
            };
            ctorScope.Declare(paramSymbol);
            _currentParameters.Add(param.Name);
        }

        // Analyze body
        if (ctor.Body != null)
        {
            AnalyzeBlock(ctor.Body);
        }

        _currentScope = prevScope;
        _currentParameters = prevParams;
    }

    // ====================
    // STATEMENT ANALYSIS
    // ====================

    private void AnalyzeBlock(BlockStatement block)
    {
        var blockScope = _currentScope.CreateChild("block");
        var prevScope = _currentScope;
        _currentScope = blockScope;

        foreach (var stmt in block.Statements)
        {
            AnalyzeStatement(stmt);
        }

        _currentScope = prevScope;
    }

    private void AnalyzeStatement(Statement stmt)
    {
        switch (stmt)
        {
            case VariableDeclarationStatement varDecl:
                AnalyzeVariableDeclaration(varDecl);
                break;

            case IfStatement ifStmt:
                AnalyzeExpression(ifStmt.Condition);
                AnalyzeStatement(ifStmt.ThenBranch);
                if (ifStmt.ElseBranch != null)
                    AnalyzeStatement(ifStmt.ElseBranch);
                break;

            case WhileStatement whileStmt:
                AnalyzeExpression(whileStmt.Condition);
                AnalyzeStatement(whileStmt.Body);
                break;

            case ForStatement forStmt:
                var forScope = _currentScope.CreateChild("for");
                var prevForScope = _currentScope;
                _currentScope = forScope;
                if (forStmt.Initializer != null)
                    AnalyzeStatement(forStmt.Initializer);
                if (forStmt.Condition != null)
                    AnalyzeExpression(forStmt.Condition);
                if (forStmt.Increment != null)
                    AnalyzeExpression(forStmt.Increment);
                AnalyzeStatement(forStmt.Body);
                _currentScope = prevForScope;
                break;

            case ForEachStatement foreachStmt:
                AnalyzeExpression(foreachStmt.Collection);
                var feScope = _currentScope.CreateChild("foreach");
                var prevFeScope = _currentScope;
                _currentScope = feScope;
                var iterType = foreachStmt.VariableType != null
                    ? ResolveTypeInfo(foreachStmt.VariableType)
                    : new TypeInfo { Name = "object" };
                feScope.Declare(new Symbol
                {
                    Name = foreachStmt.VariableName,
                    Kind = SymbolKind.Variable,
                    Type = iterType
                });
                AnalyzeStatement(foreachStmt.Body);
                _currentScope = prevFeScope;
                break;

            case ReturnStatement returnStmt:
                if (returnStmt.Value != null)
                    AnalyzeExpression(returnStmt.Value);
                break;

            case BlockStatement blockStmt:
                AnalyzeBlock(blockStmt);
                break;

            case ExpressionStatement exprStmt:
                AnalyzeExpression(exprStmt.Expression);
                break;

            case BreakStatement:
            case ContinueStatement:
                break;
        }
    }

    private void AnalyzeVariableDeclaration(VariableDeclarationStatement varDecl)
    {
        // Determine type
        TypeInfo varType;
        if (varDecl.Type != null)
        {
            varType = ResolveTypeInfo(varDecl.Type);
        }
        else if (varDecl.Initializer != null)
        {
            varType = InferType(varDecl.Initializer);
        }
        else
        {
            _diagnostics.ReportError(
                $"cannot infer type: variable '{varDecl.Name}' must have either an explicit type annotation " +
                $"or an initializer expression. Use 'int {varDecl.Name} = 0;' or 'var {varDecl.Name} = value;'.",
                varDecl.Line, varDecl.Column);
            varType = new TypeInfo { Name = "object" };
        }

        if (varDecl.Initializer != null)
        {
            AnalyzeExpression(varDecl.Initializer);

            // Type compatibility check: only when an explicit type is declared
            if (varDecl.Type != null)
            {
                var initType = InferType(varDecl.Initializer);
                CheckTypeCompatibility(varType, initType, varDecl.Name, varDecl.Line, varDecl.Column);
            }
        }

        var symbol = new Symbol
        {
            Name = varDecl.Name,
            Kind = SymbolKind.Variable,
            Type = varType,
            IsReadOnly = varDecl.IsReadOnly,
            Line = varDecl.Line,
            Column = varDecl.Column
        };

        if (!_currentScope.Declare(symbol))
        {
            _diagnostics.ReportError(
                $"duplicate variable: '{varDecl.Name}' is already declared in this scope. " +
                $"Variable names must be unique within the same block. " +
                $"Consider renaming the variable or using the existing one.",
                varDecl.Line, varDecl.Column);
        }
    }

    // ====================
    // EXPRESSION ANALYSIS
    // ====================

    private void AnalyzeExpression(Expression expr)
    {
        switch (expr)
        {
            case IdentifierExpression id:
                var sym = _currentScope.Lookup(id.Name);
                if (sym == null && !_classTable.ContainsKey(id.Name) &&
                    id.Name != "Console" && id.Name != "Math" && id.Name != "Memory")
                {
                    _diagnostics.ReportWarning(
                        $"undefined identifier: '{id.Name}' is not declared in this scope. " +
                        $"Check for typos, or ensure the variable/class is defined before use.",
                        id.Line, id.Column);
                }
                break;

            case BinaryExpression bin:
                AnalyzeExpression(bin.Left);
                AnalyzeExpression(bin.Right);
                break;

            case UnaryExpression unary:
                AnalyzeExpression(unary.Operand);
                break;

            case PostfixExpression postfix:
                AnalyzeExpression(postfix.Operand);
                break;

            case AssignmentExpression assign:
                AnalyzeExpression(assign.Target);
                AnalyzeExpression(assign.Value);
                break;

            case MethodCallExpression call:
                AnalyzeExpression(call.Target);
                foreach (var arg in call.Arguments)
                    AnalyzeExpression(arg);
                // Check if the called method is deprecated or removed
                if (call.Target is MemberAccessExpression memberCall)
                {
                    // Try to resolve the type of the target to check deprecated methods
                    var targetTypeName = InferTargetTypeName(memberCall.Target);
                    if (targetTypeName != null)
                    {
                        CheckDeprecatedUsage(targetTypeName, memberCall.MemberName, call.Line, call.Column);
                    }
                }
                break;

            case MemberAccessExpression member:
                AnalyzeExpression(member.Target);
                break;

            case ObjectCreationExpression objCreate:
                if (!_classTable.ContainsKey(objCreate.TypeName) &&
                    _globalScope.Lookup(objCreate.TypeName) == null)
                {
                    _diagnostics.ReportWarning(
                        $"undefined type: '{objCreate.TypeName}' is not a known class or type. " +
                        $"Ensure the class is defined in the same compilation unit or imported correctly.",
                        objCreate.Line, objCreate.Column);
                }
                // Check if the class being instantiated is deprecated or removed
                CheckDeprecatedUsage(objCreate.TypeName, null, objCreate.Line, objCreate.Column);
                foreach (var arg in objCreate.Arguments)
                    AnalyzeExpression(arg);
                break;

            case ArrayCreationExpression arrCreate:
                AnalyzeExpression(arrCreate.Size);
                break;

            case ArrayAccessExpression arrAccess:
                AnalyzeExpression(arrAccess.Target);
                AnalyzeExpression(arrAccess.Index);
                break;

            case CastExpression cast:
                AnalyzeExpression(cast.Expression);
                break;

            case LiteralExpression:
            case NullLiteralExpression:
            case ThisExpression:
            case BaseExpression:
                break;
        }
    }

    // ====================
    // ANNOTATION VALIDATION
    // ====================

    /// <summary>
    /// Known annotation names and their expected argument counts.
    /// </summary>
    private static readonly Dictionary<string, (int MinArgs, int MaxArgs)> KnownAnnotations = new()
    {
        ["Library"] = (2, 2),       // [@Library("Name", "Version")]
        ["Deprecated"] = (0, 2),    // [@Deprecated] or [@Deprecated("message")] or [@Deprecated("message", "1.2.0")]
        ["Removed"] = (0, 2),       // [@Removed] or [@Removed("message")] or [@Removed("message", "1.0.0")]
        ["Test"] = (0, 0),          // [@Test]
    };

    /// <summary>
    /// Validates annotations on a class or method declaration.
    /// Registers deprecated/removed entries for later usage checks.
    /// </summary>
    private void ValidateAnnotations(List<AnnotationNode> annotations, string className, string? methodName, int line, int column)
    {
        foreach (var annotation in annotations)
        {
            // Validate known annotations
            if (KnownAnnotations.TryGetValue(annotation.Name, out var argInfo))
            {
                if (annotation.Arguments.Count < argInfo.MinArgs)
                {
                    _diagnostics.ReportError(
                        $"annotation '@{annotation.Name}' requires at least {argInfo.MinArgs} argument(s), " +
                        $"but {annotation.Arguments.Count} were provided.",
                        annotation.Line, annotation.Column);
                }
                else if (annotation.Arguments.Count > argInfo.MaxArgs)
                {
                    _diagnostics.ReportError(
                        $"annotation '@{annotation.Name}' accepts at most {argInfo.MaxArgs} argument(s), " +
                        $"but {annotation.Arguments.Count} were provided.",
                        annotation.Line, annotation.Column);
                }
            }

            // Track @Deprecated
            if (annotation.Name == "Deprecated")
            {
                string? message = annotation.Arguments.Count > 0 ? GetStringArg(annotation.Arguments[0]) : null;
                string? removalVersion = annotation.Arguments.Count > 1 ? GetStringArg(annotation.Arguments[1]) : null;

                var key = methodName != null ? $"{className}.{methodName}" : className;
                if (methodName != null)
                    _deprecatedMethods[key] = (message, removalVersion);
                else
                    _deprecatedClasses[key] = (message, removalVersion);

                // Emit info diagnostic about the deprecation
                var target = methodName != null ? $"method '{className}.{methodName}'" : $"class '{className}'";
                var versionInfo = removalVersion != null ? $" Will be removed in version {removalVersion}." : "";
                var msgInfo = message != null ? $" {message}" : "";
                _diagnostics.ReportInfo(
                    $"{target} is marked as deprecated.{msgInfo}{versionInfo}",
                    annotation.Line, annotation.Column);
            }

            // Track @Removed
            if (annotation.Name == "Removed")
            {
                string? message = annotation.Arguments.Count > 0 ? GetStringArg(annotation.Arguments[0]) : null;
                string? version = annotation.Arguments.Count > 1 ? GetStringArg(annotation.Arguments[1]) : null;

                var key = methodName != null ? $"{className}.{methodName}" : className;
                if (methodName != null)
                    _removedMethods[key] = (message, version);
                else
                    _removedClasses[key] = (message, version);

                // Emit error: removed members cannot be used
                var target = methodName != null ? $"method '{className}.{methodName}'" : $"class '{className}'";
                var versionInfo = version != null ? $" (removed in version {version})" : "";
                var msgInfo = message != null ? $" {message}" : "";
                _diagnostics.ReportError(
                    $"{target} is marked as removed and cannot be used.{msgInfo}{versionInfo}",
                    annotation.Line, annotation.Column);
            }

            // Validate @Deprecated and @Removed are not both present
            var hasDeprecated = annotations.Any(a => a.Name == "Deprecated");
            var hasRemoved = annotations.Any(a => a.Name == "Removed");
            if (hasDeprecated && hasRemoved)
            {
                _diagnostics.ReportError(
                    $"a declaration cannot be both '@Deprecated' and '@Removed'. " +
                    $"Use '@Deprecated' for elements that still work but will be removed, " +
                    $"or '@Removed' for elements that can no longer be used.",
                    annotation.Line, annotation.Column);
            }
        }
    }

    /// <summary>
    /// Extracts a string value from a literal expression argument.
    /// </summary>
    private static string? GetStringArg(Expression expr)
    {
        if (expr is LiteralExpression lit && lit.LiteralType == LiteralType.String)
            return lit.Value?.ToString();
        return expr.ToString();
    }

    /// <summary>
    /// Checks if usage of a class or method is deprecated/removed and emits diagnostics.
    /// </summary>
    private void CheckDeprecatedUsage(string typeName, string? methodName, int line, int column)
    {
        // Check class-level deprecation
        if (_deprecatedClasses.TryGetValue(typeName, out var classDeprecation))
        {
            var msg = classDeprecation.Message != null ? $" {classDeprecation.Message}" : "";
            var ver = classDeprecation.RemovalVersion != null ? $" Will be removed in version {classDeprecation.RemovalVersion}." : "";
            _diagnostics.ReportWarning(
                $"class '{typeName}' is deprecated.{msg}{ver}",
                line, column);
        }

        // Check class-level removal
        if (_removedClasses.TryGetValue(typeName, out var classRemoval))
        {
            var msg = classRemoval.Message != null ? $" {classRemoval.Message}" : "";
            var ver = classRemoval.Version != null ? $" (removed in {classRemoval.Version})" : "";
            _diagnostics.ReportError(
                $"class '{typeName}' has been removed and cannot be used.{msg}{ver}",
                line, column);
        }

        // Check method-level deprecation
        if (methodName != null)
        {
            var methodKey = $"{typeName}.{methodName}";
            if (_deprecatedMethods.TryGetValue(methodKey, out var methodDeprecation))
            {
                var msg = methodDeprecation.Message != null ? $" {methodDeprecation.Message}" : "";
                var ver = methodDeprecation.RemovalVersion != null ? $" Will be removed in version {methodDeprecation.RemovalVersion}." : "";
                _diagnostics.ReportWarning(
                    $"method '{typeName}.{methodName}()' is deprecated.{msg}{ver}",
                    line, column);
            }

            // Check method-level removal
            if (_removedMethods.TryGetValue(methodKey, out var methodRemoval))
            {
                var msg = methodRemoval.Message != null ? $" {methodRemoval.Message}" : "";
                var ver = methodRemoval.Version != null ? $" (removed in {methodRemoval.Version})" : "";
                _diagnostics.ReportError(
                    $"method '{typeName}.{methodName}()' has been removed and cannot be used.{msg}{ver}",
                    line, column);
            }
        }
    }

    // ====================
    // TYPE COMPATIBILITY
    // ====================

    /// <summary>
    /// Checks whether the initializer type is compatible with the declared type.
    /// Reports an error if types are incompatible.
    /// </summary>
    private void CheckTypeCompatibility(TypeInfo declaredType, TypeInfo initType, string varName, int line, int column)
    {
        // Skip check if either type is unknown/object (can't verify)
        if (declaredType.Name == "object" || initType.Name == "object")
            return;

        // Skip check if either is void (already invalid elsewhere)
        if (declaredType.IsVoid || initType.IsVoid)
            return;

        // Array compatibility: both must be arrays or both non-arrays
        if (declaredType.IsArray != initType.IsArray)
        {
            _diagnostics.ReportError(
                $"type mismatch: cannot assign '{initType}' to variable '{varName}' of type '{declaredType}'. " +
                $"Array and non-array types are not compatible.",
                line, column);
            return;
        }

        // Exact match
        if (declaredType.Name == initType.Name)
            return;

        // Implicit numeric widening conversions allowed:
        //   byte -> int, long, float, double
        //   int -> long, float, double
        //   long -> float, double
        //   float -> double
        if (IsImplicitNumericConversion(initType.Name, declaredType.Name))
            return;

        // null can be assigned to nullable types, strings, and reference types
        if (initType.IsNullable && !declaredType.IsPrimitive)
            return;

        // Everything else is a type mismatch
        _diagnostics.ReportError(
            $"type mismatch: cannot assign '{initType}' to variable '{varName}' of type '{declaredType}'. " +
            $"The types are not compatible. Use an explicit cast if this is intentional: ({declaredType}) value.",
            line, column);
    }

    /// <summary>
    /// Determines whether an implicit numeric conversion from sourceType to targetType is allowed.
    /// </summary>
    private static bool IsImplicitNumericConversion(string from, string to)
    {
        return (from, to) switch
        {
            ("byte", "int" or "long" or "float" or "double") => true,
            ("int", "long" or "float" or "double") => true,
            ("long", "float" or "double") => true,
            ("float", "double") => true,
            _ => false
        };
    }

    // ====================
    // TYPE RESOLUTION
    // ====================

    private TypeInfo ResolveTypeInfo(TypeReference? typeRef)
    {
        if (typeRef == null)
            return new TypeInfo { Name = "void" };

        return new TypeInfo
        {
            Name = typeRef.Name,
            IsArray = typeRef.IsArray,
            IsNullable = typeRef.IsNullable
        };
    }

    private TypeInfo InferType(Expression expr)
    {
        return expr switch
        {
            LiteralExpression lit => lit.LiteralType switch
            {
                LiteralType.Integer => new TypeInfo { Name = "int" },
                LiteralType.Float => new TypeInfo { Name = "double" },
                LiteralType.String => new TypeInfo { Name = "string" },
                LiteralType.Char => new TypeInfo { Name = "char" },
                LiteralType.Boolean => new TypeInfo { Name = "bool" },
                _ => new TypeInfo { Name = "object" }
            },
            NullLiteralExpression => new TypeInfo { Name = "object", IsNullable = true },
            ObjectCreationExpression obj => new TypeInfo { Name = obj.TypeName },
            ArrayCreationExpression arr => new TypeInfo { Name = arr.ElementType.Name, IsArray = true },
            IdentifierExpression id => _currentScope.Lookup(id.Name)?.Type ?? new TypeInfo { Name = "object" },
            _ => new TypeInfo { Name = "object" }
        };
    }

    /// <summary>
    /// Infers the type name of an expression target for deprecated/removed checks.
    /// </summary>
    private string? InferTargetTypeName(Expression expr)
    {
        return expr switch
        {
            IdentifierExpression id => _currentScope.Lookup(id.Name)?.Type.Name
                                       ?? (_classTable.ContainsKey(id.Name) ? id.Name : null),
            ObjectCreationExpression obj => obj.TypeName,
            ThisExpression => _currentClassName,
            _ => null
        };
    }
}
