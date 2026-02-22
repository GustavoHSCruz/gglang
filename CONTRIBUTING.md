# Contributing to ggLang

Thank you for your interest in contributing to ggLang! This guide covers the process for
setting up the project, making changes, and submitting contributions.

## Prerequisites

- [.NET 10+ SDK](https://dotnet.microsoft.com/)
- [GCC](https://gcc.gnu.org/) (any recent version)
- Linux x86-64 (primary target)
- Git

## Getting Started

```bash
# Clone the repository
git clone https://github.com/gglang/gglang && cd gglang

# Build the compiler
dotnet build

# Run tests
dotnet test

# Build and run examples
make examples
```

## Project Structure

```
ggLang/
├── src/
│   ├── ggLang.CLI/            # CLI entry point (gg command)
│   └── ggLang.Compiler/       # Compiler core
│       ├── Lexer/              # Tokenization
│       ├── Parser/             # Recursive descent parser + AST
│       ├── Analysis/           # Semantic analyzer + diagnostics
│       └── CodeGen/            # C code generator + GCC backend
├── tests/ggLang.Tests/        # xUnit tests
├── runtime/                   # C runtime (gg_runtime.h/.c)
├── libs/                      # Standard libraries (.lib.gg)
├── examples/                  # Example .gg programs
└── docs/                      # Technical documentation
```

For a deeper dive into the compiler internals, see [docs/architecture.md](docs/architecture.md).

## Development Workflow

### 1. Create a Branch

```bash
git checkout -b feature/my-feature
```

### 2. Make Changes

- **Compiler changes** — edit files in `src/ggLang.Compiler/`
- **CLI changes** — edit `src/ggLang.CLI/Program.cs`
- **Runtime changes** — edit `runtime/gg_runtime.c` and `runtime/gg_runtime.h`
- **Standard libraries** — edit or add `.lib.gg` files in `libs/`

### 3. Test

```bash
# Run unit tests
dotnet test

# Build and verify examples
make examples

# Test a specific .gg file
dotnet run --project src/ggLang.CLI -- run examples/hello.gg
```

### 4. Submit a Pull Request

- Write a clear title and description
- Ensure all tests pass
- Add tests for new features
- Update documentation if relevant

## Code Style

We follow the conventions defined in [STANDARDS.md](STANDARDS.md) for ggLang source code.

For the C# compiler codebase:

- **Naming**: PascalCase for public members, \_camelCase for private fields
- **Formatting**: Standard C# conventions with 4-space indentation
- **XML docs**: Add `/// <summary>` on public methods and classes
- **Null safety**: Use nullable annotations (`?`) and check for nulls

## Adding a New Language Feature

1. **Lexer** — add new token types in `TokenType.cs`, update `GgLexer` to recognize them
2. **Parser** — add AST node types, update `GgParser` to parse the new syntax
3. **Semantic Analyzer** — add validation rules in `SemanticAnalyzer.cs`
4. **Code Generator** — add C code emission in `CCodeGenerator.cs`
5. **Runtime** — if needed, add C functions in `gg_runtime.c`/`.h`
6. **Tests** — add tests for each stage of the pipeline
7. **Examples** — add an example `.gg` file demonstrating the feature
8. **Documentation** — update `docs/language-spec.md`

## Adding a Standard Library

1. Create `libs/YourLib.lib.gg` with `[@Library("YourLib", "1.0.0")]` annotation
2. Add C runtime implementations in `gg_runtime.c`
3. Add function declarations in `gg_runtime.h`
4. Add an example demonstrating usage
5. Update `README.md` library table

## Reporting Issues

- Use GitHub Issues
- Include: ggLang version, OS, GCC version, minimal reproducing `.gg` file, expected vs actual behavior

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
