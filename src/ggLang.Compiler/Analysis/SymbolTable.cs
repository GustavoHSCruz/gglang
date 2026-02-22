namespace ggLang.Compiler.Analysis;

/// <summary>
/// Scoped symbol table for semantic analysis.
/// Supports nested scopes (class → method → block → ...).
/// </summary>
public sealed class SymbolTable
{
    private readonly Dictionary<string, Symbol> _symbols = [];
    private readonly SymbolTable? _parent;
    private readonly List<SymbolTable> _children = [];

    public string ScopeName { get; }
    public SymbolTable? Parent => _parent;
    public IReadOnlyList<SymbolTable> Children => _children;

    /// <summary>
    /// Creates a root-level symbol table.
    /// </summary>
    public SymbolTable(string scopeName = "global")
    {
        ScopeName = scopeName;
        _parent = null;
    }

    /// <summary>
    /// Creates a child scope.
    /// </summary>
    private SymbolTable(string scopeName, SymbolTable parent)
    {
        ScopeName = scopeName;
        _parent = parent;
    }

    /// <summary>
    /// Creates and returns a new child scope.
    /// </summary>
    public SymbolTable CreateChild(string scopeName)
    {
        var child = new SymbolTable(scopeName, this);
        _children.Add(child);
        return child;
    }

    /// <summary>
    /// Declares a new symbol in the current scope.
    /// Returns true if successful, false if already declared.
    /// </summary>
    public bool Declare(Symbol symbol)
    {
        return _symbols.TryAdd(symbol.Name, symbol);
    }

    /// <summary>
    /// Looks up a symbol in this scope and all parent scopes.
    /// </summary>
    public Symbol? Lookup(string name)
    {
        if (_symbols.TryGetValue(name, out var symbol))
            return symbol;
        return _parent?.Lookup(name);
    }

    /// <summary>
    /// Looks up a symbol only in this scope (no parent traversal).
    /// </summary>
    public Symbol? LookupLocal(string name)
    {
        _symbols.TryGetValue(name, out var symbol);
        return symbol;
    }

    /// <summary>
    /// Returns all symbols in the current scope.
    /// </summary>
    public IEnumerable<Symbol> GetLocalSymbols() => _symbols.Values;

    /// <summary>
    /// Checks if a type name is a primitive/builtin type.
    /// </summary>
    public static bool IsBuiltinType(string name)
    {
        return name is "int" or "float" or "double" or "long" or "byte" or "short"
                    or "bool" or "char" or "string" or "void" or "object";
    }

    /// <summary>
    /// Checks if typeA is a subtype of typeB.
    /// Handles: identity, numeric widening, object supertype, inheritance.
    /// </summary>
    public bool IsSubtypeOf(TypeInfo typeA, TypeInfo typeB, Dictionary<string, ClassInfo> classTable)
    {
        if (typeA.Equals(typeB)) return true;
        if (typeB.Name == "object") return true;

        // Numeric widening: int → long, int → float, int → double, float → double
        if (typeA.IsNumeric && typeB.IsNumeric)
        {
            return (typeA.Name, typeB.Name) switch
            {
                ("byte", "short" or "int" or "long" or "float" or "double") => true,
                ("short", "int" or "long" or "float" or "double") => true,
                ("int", "long" or "float" or "double") => true,
                ("long", "float" or "double") => true,
                ("float", "double") => true,
                _ => false
            };
        }

        // Null literal of nullable type
        if (typeA.Name == "null" && typeB.IsNullable) return true;
        if (typeA.Name == "null" && !typeB.IsPrimitive) return true;

        // Inheritance chain
        if (classTable.TryGetValue(typeA.Name, out var classInfo))
        {
            // Walk up inheritance chain
            var current = classInfo;
            while (current?.BaseClassName != null)
            {
                if (current.BaseClassName == typeB.Name) return true;
                classTable.TryGetValue(current.BaseClassName, out current);
            }

            // Interface check
            if (classInfo.Interfaces.Contains(typeB.Name)) return true;

            // Also check parent class interfaces
            current = classInfo;
            while (current?.BaseClassName != null)
            {
                if (classTable.TryGetValue(current.BaseClassName, out var parentClass))
                {
                    if (parentClass.Interfaces.Contains(typeB.Name)) return true;
                    current = parentClass;
                }
                else break;
            }
        }

        return false;
    }

    /// <summary>
    /// Registers built-in types and functions in the global scope.
    /// </summary>
    public void RegisterBuiltinTypes()
    {
        // Console class with static methods
        var consoleSymbol = new Symbol
        {
            Name = "Console",
            Kind = SymbolKind.Class,
            Type = new TypeInfo { Name = "Console" },
            IsPublic = true,
            IsStatic = true
        };
        Declare(consoleSymbol);

        // Math class
        var mathSymbol = new Symbol
        {
            Name = "Math",
            Kind = SymbolKind.Class,
            Type = new TypeInfo { Name = "Math" },
            IsPublic = true,
            IsStatic = true
        };
        Declare(mathSymbol);
    }
}

/// <summary>
/// Stores metadata about a class for inheritance resolution.
/// </summary>
public class ClassInfo
{
    public required string Name { get; init; }
    public string? BaseClassName { get; set; }
    public List<string> Interfaces { get; init; } = [];
    public Dictionary<string, Symbol> Fields { get; init; } = [];
    public Dictionary<string, Symbol> Methods { get; init; } = [];
    public bool IsAbstract { get; init; }
    public bool IsSealed { get; init; }
    public bool HasConstructor { get; set; }
}
