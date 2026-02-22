# ggLang CLI Reference

This document covers the `gg` command-line interface, including project initialization and package workflow.

## Command Summary

| Command | Description |
| --- | --- |
| `gg build <file.gg> [-o output]` | Compile a `.gg` file to a native binary |
| `gg run <file.gg>` | Build and run immediately |
| `gg check <file.gg>` | Run lexer/parser/semantic checks without native compilation |
| `gg emit-c <file.gg> [-o out.c]` | Emit generated C code |
| `gg init [name] [--mem <size>] [--no-gc]` | Create a new project scaffold |
| `gg pkg <subcommand>` | Manage `.lib.gg` dependencies |
| `gg version` | Show compiler version |
| `gg help` | Show CLI help |

## Core Commands

### `gg build`

```bash
gg build src/main.gg -o my_app
```

- Runs full pipeline: lexer -> parser -> semantic analyzer -> C codegen -> GCC.
- Writes a native executable.

### `gg run`

```bash
gg run src/main.gg
```

- Builds to a temporary binary and executes it.

### `gg check`

```bash
gg check src/main.gg
```

- Validates source and prints diagnostics.
- Does not emit C or native binaries.

### `gg emit-c`

```bash
gg emit-c src/main.gg -o out/main.c
```

- Generates C output for debugging or inspection.

## Project Initialization

### `gg init`

```bash
gg init my_project
gg init --mem 512M
gg init embedded_app --no-gc
```

Creates:

- `src/main.gg`
- `libs/`
- `.gitignore`
- `README.md`
- Optional `gg.config` when `--mem` or `--no-gc` is used

Options:

- `--mem <size>`: sets runtime memory limit (`B`, `K/KB`, `M/MB`, `G/GB`)
- `--no-gc` or `--no-garbage-collector`: disables garbage collector
- `--mem` and `--no-gc` are mutually exclusive

## Package Manager

Use `gg pkg` to manage project-local libraries.

| Subcommand | Description |
| --- | --- |
| `gg pkg init` | Create `gg.json` manifest |
| `gg pkg install <name> [version]` | Install package (currently standard libs/local sources) |
| `gg pkg list` | List installed libraries in local `libs/` |
| `gg pkg remove <name>` | Remove a local library (auto-unlocks locked libs first) |
| `gg pkg update` | Update local libraries from available standard lib sources (auto unlock/relock) |
| `gg pkg search [query]` | Search available package names |
| `gg pkg publish` | Placeholder until registry is available |

Supported aliases:

- `install`: `add`
- `list`: `ls`
- `remove`: `rm`, `uninstall`
- `update`: `upgrade`
- `search`: `find`

Behavior notes:

- Installed `.lib.gg` files are forced to read-only.
- On Linux/macOS, CLI attempts immutable lock (`chattr +i` / `chflags uchg`) when available.
- `gg pkg update` and `gg pkg remove` automatically unlock and relock files as needed.
- Set `GG_STDLIB_DIR` to override standard-library source lookup (useful for tests/custom installs).

## Project Configuration Files

### `gg.config`

Optional file read during `build`, `run`, and `emit-c`.

```ini
garbage_collector = enabled
memory_limit = 512M
```

Keys:

- `garbage_collector`: `enabled` or `disabled`
- `memory_limit`: `0` or sized value (`256MB`, `2G`, etc.)

### `gg.json`

Manifest used by `gg pkg`.

```json
{
  "name": "my_project",
  "version": "0.1.0",
  "main": "src/main.gg",
  "dependencies": {}
}
```

## Notes

- Files in protected standard library locations and `.lib.gg` files are treated as read-only entry-point targets.
- If GCC is missing from `PATH`, native compilation will fail for `build` and `run`.
