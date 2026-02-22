# ggLang Project - Copilot Instructions

## Project Overview

ggLang is a C#-style, statically-typed, 100% object-oriented programming language that compiles to native
binaries via C transpilation (GCC backend). The compiler is written in C# (.NET 10).

## Architecture

The compiler follows a traditional pipeline:

```
.gg source → Lexer → Parser (AST) → Semantic Analyzer → C Code Generator → GCC → Native Binary
```

### Key Components

- **Lexer** (`src/ggLang.Compiler/Lexer/`): Tokenizes source files into a stream of tokens.
- **Parser** (`src/ggLang.Compiler/Parser/`): Recursive descent parser producing an AST.
- **AST** (`src/ggLang.Compiler/Parser/Ast/`): Node hierarchy representing all language constructs.
- **Semantic Analyzer** (`src/ggLang.Compiler/Analysis/`): 3-pass analysis: types → members → bodies.
- **C Code Generator** (`src/ggLang.Compiler/CodeGen/`): Emits C code with struct-based OOP and vtables.
- **CLI** (`src/ggLang.CLI/`): Command-line front-end (build, run, check, emit-c, init).
- **Runtime** (`runtime/`): Minimal C runtime library (`gg_runtime.h`, `gg_runtime.c`).
- **Standard Libraries** (`libs/`): Built-in `.lib.gg` libraries installed with the compiler.
- **Tests** (`tests/ggLang.Tests/`): xUnit tests for lexer and parser.

## Language Syntax Rules

ggLang uses **C#-style syntax** — type before name, no `func` keyword:

```csharp
// Variable declaration
int x = 42;
string name = "hello";
var inferred = 3.14;  // type inference

// Method declaration (return type before name)
int add(int a, int b) { return a + b; }
void print(string msg) { Console.writeLine(msg); }

// Constructor (class name, not 'constructor' keyword)
Person(string name, int age) { this.name = name; this.age = age; }

// Inheritance
class Dog : Animal { ... }

// Constructor with base call
Dog(string name) : base(name) { }

// Annotations
[@Library("Math", "1.0.0")]
class Math { ... }

[@Deprecated("Use newMethod instead")]
void oldMethod() { ... }
```

## Annotations

ggLang supports annotations using the `[@Name(args)]` syntax:

- Annotations are placed before class or method declarations.
- Syntax: `[@AnnotationName]` or `[@AnnotationName("arg1", "arg2")]`
- Built-in annotations:
  - `[@Library("Name", "Version")]` — marks a class as a standard library.
  - More annotations will be added as the language evolves.

## Standard Libraries

- Libraries live in the `libs/` directory and are installed to `/usr/local/lib/gglang/libs/`.
- File naming convention: `LibraryName.lib.gg` (e.g., `Math.lib.gg`).
- Each library file must have a `[@Library("Name", "Version")]` annotation on its main class.
- Libraries are pure ggLang code — no FFI or external dependencies.

## Coding Standards

1. **Language**: All code, comments, error messages, and documentation must be in **English**.
2. **Naming**: Follow C# conventions (PascalCase for types/methods, camelCase for locals/params).
3. **No `func` keyword**: Methods are declared as `ReturnType name(params)`.
4. **No `constructor` keyword**: Constructors use the class name.
5. **Type before name**: Always `Type name`, never `name: Type`.
6. **Parameters**: Always `Type name`, never `name: Type`.

## OOP Implementation in C

The C code generator implements OOP using:

- **Structs** for class instances (fields + vtable pointer)
- **VTables** for virtual method dispatch
- **Wrapper functions** for inherited virtual methods (type casting)
- **Factory functions** (`ClassName_create()`) for object allocation
- **Constructor functions** (`ClassName_construct()`) for initialization

### Inheritance Chain Resolution

When resolving virtual methods, the code generator walks the full inheritance chain
from derived to base class. Wrapper functions handle pointer type casting between
parent and child struct types.

## Important Considerations

- The parser uses **lookahead** to distinguish between methods, fields, and constructors
  (all start with a type token or class name).
- The semantic analyzer performs **3 passes** to handle forward references.
- **All code must be inside classes** — ggLang is 100% OOP.
- Error recovery in the parser uses simple skip-to-next-member/statement strategy.
- The `var` keyword is kept for local type inference only.

## Testing

```bash
dotnet test                                    # Run all tests
dotnet run --project src/ggLang.CLI -- build examples/hello.gg  # Build example
dotnet run --project src/ggLang.CLI -- run examples/hello.gg    # Build & run
```

## File Conventions

- Source files: `.gg` extension
- One main class per file (by convention, not enforced)
- Entry point: `static void main()` in any class named `Program` (or any class with main)

## Changelog

**RULE**: Whenever changes are made to the project, update `CHANGELOG.md` with:

- A brief description of each change.
- The current date and time obtained from the operating system (use `date` command or equivalent).
- Follow the [Keep a Changelog](https://keepachangelog.com/) format.
- Group changes under: Added, Changed, Fixed, Removed, Deprecated.
- Always use the format: `## [version]`

## Version Synchronization

When updating the project version, update all version declarations in the same PR:

- `src/ggLang.CLI/Program.cs` (`Version` constant used by `gg version`)
- `Makefile` install banner version
- `build.sh` `VERSION` variable
- `CHANGELOG.md` version header

Never leave these files with different version values.

## Commit Message Rules

All commit messages must follow the **Conventional Commits** specification. This ensures consistency and clarity in the commit history.

### Conventional Commits Format

A commit message must be structured as follows:

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

#### Types
- **feat**: A new feature
- **fix**: A bug fix
- **docs**: Documentation changes
- **style**: Code style changes (formatting, missing semi-colons, etc.)
- **refactor**: Code refactoring (neither fixes a bug nor adds a feature)
- **perf**: Performance improvements
- **test**: Adding or updating tests
- **build**: Changes to the build system or dependencies
- **ci**: Changes to CI configuration files and scripts
- **chore**: Other changes that don't modify src or test files
- **revert**: Reverts a previous commit

#### Examples
- `feat(parser): add support for annotations`
- `fix(lexer): handle null characters correctly`
- `docs: update README with new examples`

### Additional Notes
- Use the imperative mood in the description (e.g., "add feature" not "added feature").
- Limit the subject line to 72 characters.
- Wrap the body at 80 characters.
- Include references to issues or pull requests in the footer when applicable.
