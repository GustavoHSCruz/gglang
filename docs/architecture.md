# ggLang Compiler Architecture

This document describes the internal architecture of the ggLang compiler for contributors who want
to understand, debug, or extend the codebase.

## Compilation Pipeline

```
.gg source → Lexer → Parser → Semantic Analyzer → C Code Generator → GCC → Native Binary
```

Each stage transforms the input and feeds its output to the next. The overall flow is
orchestrated by `ggLang.CLI/Program.cs`.

## Project Layout

| Project           | Path                   | Purpose                                               |
| ----------------- | ---------------------- | ----------------------------------------------------- |
| `ggLang.CLI`      | `src/ggLang.CLI/`      | CLI entry point — parses arguments, runs the pipeline |
| `ggLang.Compiler` | `src/ggLang.Compiler/` | All compiler phases (library)                         |
| `ggLang.Tests`    | `tests/ggLang.Tests/`  | xUnit test suite                                      |

## Pipeline Stages

### 1. Lexer (`Compiler/Lexer/`)

**Input**: Raw source string
**Output**: `List<Token>`

- `GgLexer` scans the source character-by-character
- Produces tokens with `TokenType`, `Value`, `Line`, `Column`
- Handles keywords, identifiers, literals (int, float, string, char, bool), operators, comments
- Skips whitespace and comments (`//` line, `/* */` block)
- Reports errors for malformed tokens (e.g., unterminated strings)

Key types: `GgLexer`, `Token`, `TokenType`

### 2. Parser (`Compiler/Parser/`)

**Input**: `List<Token>`
**Output**: `CompilationUnit` (AST root)

- `GgParser` uses recursive descent with backtracking
- Produces an AST (Abstract Syntax Tree) rooted at `CompilationUnit`
- Handles: classes, interfaces, enums, methods, constructors, fields,
  control flow (if/else, for, while, foreach), expressions, annotations

Key types: `GgParser`, `CompilationUnit`, `ClassDeclaration`, `MethodDeclaration`,
`Expression`, `Statement`, and all nodes in `Parser/Ast/`

### 3. Semantic Analyzer (`Compiler/Analysis/`)

**Input**: `CompilationUnit`
**Output**: Populated `ClassTable` + `DiagnosticBag`

Performs 3 passes over the AST:

1. **Pass 1 — Register Types**: Walks all top-level declarations, registers classes, interfaces,
   and enums in the symbol table. Detects duplicate type names.
2. **Pass 2 — Register Members**: Registers fields, methods, and constructors for each class.
   Resolves inheritance chains (copies base class members to derived classes).
3. **Pass 3 — Analyze Bodies**: Walks method and constructor bodies. Checks variable declarations,
   scoping, identifier resolution. Reports warnings for undefined identifiers.

Key types: `SemanticAnalyzer`, `DiagnosticBag`, `Diagnostic`, `SymbolTable`, `Symbol`, `ClassInfo`

### 4. C Code Generator (`Compiler/CodeGen/CCodeGenerator.cs`)

**Input**: `CompilationUnit` + `SemanticAnalyzer`
**Output**: C source code string

Generates ANSI C code implementing the ggLang program:

- **Structs**: Each class becomes a C struct with a vtable pointer and fields
- **VTables**: Virtual method dispatch via function pointer tables
- **Constructors**: `ClassName_construct(self, ...)` + `ClassName_create(...)` factory
- **Methods**: `ClassName_methodName(self, ...)` functions
- **Inheritance**: Base class fields are embedded, base constructor called first
- **Console I/O**: `Console.writeLine(x)` → `printf()` with type-aware format specifiers
- **Math**: `Math.abs(x)` → `abs(x)` / `fabs(x)` depending on type
- **Extension Methods**: `expr.method()` → `gg_ext_{type}_{method}(expr)`
- **GC roots**: emits automatic root frames (`gg_gc_push_root_frame` / `gg_gc_pop_root_frame`) and auto-registers local/parameter reference roots
- **Write barrier hook**: reference assignments in statement context call `gg_gc_write_barrier(...)`

The output is split into 4 `StringBuilder` sections: `_header`, `_structs`, `_prototypes`, `_implementations`.

### 5. Native Compiler (`Compiler/CodeGen/NativeCompiler.cs`)

**Input**: C source file path
**Output**: Native binary

- Wraps GCC to compile the generated C code
- Links the C runtime (`gg_runtime.c`) and math library (`-lm`)
- Adds `-pthread` on POSIX targets for runtime GC synchronization
- Supports Windows, Linux, and macOS
- Provides `Compile()` and `Run()` methods

## C Runtime (`runtime/`)

The C runtime provides:

- **Memory management**: `gg_alloc()`, `gg_free()` with mark-and-sweep GC, automatic root frames, and configurable memory limit (`gg_gc_set_memory_limit`)
- **GC concurrency hardening**: global GC state is protected by a runtime mutex
- **String operations**: concatenation, comparison, conversion
- **Console I/O**: `gg_console_readline()`, etc.
- **Standard library implementations**: Hash maps, linked lists, stacks, queues, crypto, file I/O, networking, OS functions
- **Extension methods**: `gg_ext_{type}_{method}()` implementations for all primitive types

## Standard Libraries (`libs/`)

Libraries are `.lib.gg` files with `[@Library("Name", "Version")]` annotations.
They define ggLang-level APIs that map to C runtime functions.
Libraries are read-only — users import them but cannot compile them directly.

## How to Add a Feature

See [CONTRIBUTING.md](../CONTRIBUTING.md#adding-a-new-language-feature) for the step-by-step process.
