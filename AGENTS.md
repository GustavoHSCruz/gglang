# ggLang Project - Agent Instructions

This file defines project-specific rules for coding agents (Codex, Copilot, etc.).
It is based on `.github/copilot-instructions.md`, adapted to current repository state.

## Project Overview

ggLang is a statically typed, C#-style, 100% object-oriented language that compiles
to native binaries through C transpilation (GCC backend). The compiler is built with
C# on .NET 10.

## Architecture

Compilation pipeline:

```text
.gg source -> Lexer -> Parser (AST) -> Semantic Analyzer -> C Code Generator -> GCC -> Native Binary
```

Key components:

- `src/ggLang.Compiler/Lexer/`: tokenization
- `src/ggLang.Compiler/Parser/`: recursive descent parser + AST builder
- `src/ggLang.Compiler/Parser/Ast/`: AST node hierarchy
- `src/ggLang.Compiler/Analysis/`: semantic analysis and diagnostics
- `src/ggLang.Compiler/CodeGen/`: C code generation + native compilation wrapper
- `src/ggLang.CLI/`: `gg` command-line interface (`build`, `run`, `check`, `emit-c`, `init`, `pkg`)
- `runtime/`: C runtime (`gg_runtime.h`, `gg_runtime.c`)
- `libs/`: standard libraries (`*.lib.gg`)
- `tests/ggLang.Tests/`: unit and end-to-end tests

## ggLang Syntax Rules

Always follow ggLang syntax conventions:

- Type before name: `int x = 1;`
- No `func` keyword
- No `constructor` keyword (constructor name must match class name)
- Parameter format is `Type name`
- `var` only for local type inference
- All code must be inside classes

Examples:

```csharp
int add(int a, int b) { return a + b; }

class Dog : Animal {
    Dog(string name) : base(name) { }
}
```

## Annotations

Annotations use `[@Name(args)]` and appear before class/method declarations.

Known annotations include:

- `[@Library("Name", "Version")]`
- `[@Deprecated("message", "version")]`
- `[@Removed("message", "version")]`
- `[@Test]`

When editing analyzer/parser behavior, keep annotation argument validation in sync.

## Standard Library Rules

- Standard libraries are `.lib.gg` files in `libs/`.
- Naming convention: `LibraryName.lib.gg`
- Main library class must include `[@Library("Name", "Version")]`.
- Standard libraries are treated as read-only entry-point targets by CLI safeguards.

## Coding Standards

1. Write code, comments, diagnostics, and technical docs in English.
2. Follow C# naming conventions:
   - PascalCase for types/methods/properties
   - camelCase for locals/parameters
3. Prefer clear diagnostics with actionable guidance.
4. Keep pipeline changes coherent across Lexer, Parser, Semantic Analyzer, and CodeGen.

## OOP Codegen Constraints

C output represents OOP using:

- Structs for instances
- VTables for virtual dispatch
- Wrapper functions for inheritance/virtual overrides
- `ClassName_create()` factory allocation
- `ClassName_construct()` initialization

When changing inheritance or virtual dispatch, verify full chain resolution from
derived to base.

## Testing Expectations

Run relevant tests when changing compiler/runtime/CLI behavior:

```bash
dotnet test
dotnet run --project src/ggLang.CLI -- run examples/hello.gg
```

If GCC-dependent tests cannot run in the current environment, state that clearly.

## Changelog Rule

For any change, update `CHANGELOG.md` using Keep a Changelog style.

Required format:

- Version/date header: `## [version] - YYYY-MM-DD HH:MM`
- Groups: `Added`, `Changed`, `Fixed`, `Removed`, `Deprecated`
- Use system date/time (`date` command).

## Commit Message Rule

Use Conventional Commits:

```text
<type>[optional scope]: <description>
```

Allowed types: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`,
`build`, `ci`, `chore`, `revert`.

Guidelines:

- Imperative mood in subject
- Subject <= 72 chars
- Body wrapped near 80 chars when needed

## References

- Base source: `.github/copilot-instructions.md`
- Language spec: `docs/language-spec.md`
- Architecture details: `docs/architecture.md`
- CLI details: `docs/cli-reference.md`
