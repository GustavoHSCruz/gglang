namespace ggLang.Compiler.Analysis;

/// <summary>
/// Kind of symbol in the symbol table.
/// </summary>
public enum SymbolKind
{
    Variable,
    Parameter,
    Field,
    Method,
    Constructor,
    Class,
    Interface,
    Enum,
    Module
}

/// <summary>
/// Represents a named symbol in the program (variable, method, class, etc).
/// </summary>
public class Symbol
{
    public required string Name { get; init; }
    public required SymbolKind Kind { get; init; }
    public required TypeInfo Type { get; set; }

    /// <summary>Whether this symbol is publicly accessible.</summary>
    public bool IsPublic { get; init; }

    /// <summary>Whether this symbol is static (class-level).</summary>
    public bool IsStatic { get; init; }

    /// <summary>Whether this symbol is read-only.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Line where this symbol was declared.</summary>
    public int Line { get; init; }

    /// <summary>Column where this symbol was declared.</summary>
    public int Column { get; init; }

    public override string ToString() => $"{Kind} {Name}: {Type}";
}

/// <summary>
/// Represents type information for a symbol.
/// </summary>
public class TypeInfo
{
    public string Name { get; set; } = "void";
    public bool IsArray { get; set; }
    public bool IsNullable { get; set; }
    public bool IsVoid => Name == "void";
    public bool IsNumeric => Name is "int" or "float" or "double" or "long" or "byte" or "short";
    public bool IsPrimitive => Name is "int" or "float" or "double" or "long" or "byte" or "short"
                                    or "bool" or "char" or "string" or "void";

    public override string ToString()
    {
        var result = Name;
        if (IsArray) result += "[]";
        if (IsNullable) result += "?";
        return result;
    }

    public override bool Equals(object? obj) =>
        obj is TypeInfo other && Name == other.Name && IsArray == other.IsArray;

    public override int GetHashCode() => HashCode.Combine(Name, IsArray);
}
