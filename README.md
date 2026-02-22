# ggLang

ggLang is a statically typed, C#-style programming language that compiles to native binaries through C transpilation (GCC backend).

## What This Repository Contains

- `gg` CLI (`build`, `run`, `check`, `emit-c`, `init`, `pkg`)
- Compiler pipeline in C# (lexer, parser, semantic analyzer, code generator)
- C runtime used by generated programs
- Built-in standard libraries (`libs/*.lib.gg`)
- Automated tests and runnable examples

## Current Scope

- Status: beta (see [CHANGELOG.md](CHANGELOG.md))
- Toolchain: `.NET 10` + `GCC`
- Output: native executable from a `.gg` entry file

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [GCC](https://gcc.gnu.org/) available in `PATH`

## Quick Start

### Run from source (no install)

```bash
dotnet build
dotnet run --project src/ggLang.CLI -- run examples/hello.gg
```

### Install CLI (`gg`)

```bash
make install
gg version
gg run examples/hello.gg
```

### Create a new project

```bash
gg init my_app
cd my_app
gg run src/main.gg
```

## Repository Layout

```text
src/ggLang.CLI/          # CLI entry point
src/ggLang.Compiler/     # Compiler phases
runtime/                 # Runtime C layer
libs/                    # Standard libraries (.lib.gg)
examples/                # Sample programs
tests/ggLang.Tests/      # Unit and end-to-end tests
docs/                    # Technical docs
```

## Documentation

- Language specification: [docs/language-spec.md](docs/language-spec.md)
- Compiler architecture: [docs/architecture.md](docs/architecture.md)
- CLI and package manager: [docs/cli-reference.md](docs/cli-reference.md)
- Standard library overview: [docs/standard-library.md](docs/standard-library.md)
- Contributing guide: [CONTRIBUTING.md](CONTRIBUTING.md)
- Project standards: [STANDARDS.md](STANDARDS.md)
- Changelog: [CHANGELOG.md](CHANGELOG.md)

## License

MIT ([LICENSE](LICENSE))
