# Changelog

All notable changes to the ggLang project will be documented in this file.

Format: [version] - YYYY-MM-DD

---

## [0.9.0] - 2026-02-21 02:27

### Added

- **`[@Deprecated]` annotation with removal version** — classes and methods can be marked as deprecated with `[@Deprecated("message", "1.2.0")]`. The second argument specifies the version in which the element will be removed. The semantic analyzer emits info diagnostics for deprecated declarations and warnings when deprecated elements are used.
- **`[@Removed]` annotation** — marks classes and methods that have been removed and can no longer be used. Usage of removed elements produces a compilation error. `[@Deprecated]` and `[@Removed]` cannot be applied simultaneously.
- **Annotation argument validation** — the semantic analyzer now validates known annotations (`@Library`, `@Deprecated`, `@Removed`, `@Test`) for correct argument counts, reporting errors for too few or too many arguments.
- **`--mem` flag for `gg init`** — sets a maximum memory limit for the application (e.g., `gg init --mem 512M`, `gg init --mem 2G`). When exceeded at runtime, the GC forces a collection; if memory is still over the limit, the application terminates. Designed for embedded/constrained environments. Configuration stored in `gg.config`.
- **`--no-gc` / `--no-garbage-collector` flag for `gg init`** — completely disables the garbage collector. Memory must be managed manually via `Memory.free(obj)` and `Memory.alloc(size)`. Mutually exclusive with `--mem`. Uses `#define GG_NO_GC 1` with conditional compilation in the runtime.
- **`Memory.free()` and `Memory.alloc()` built-in functions** — available in both GC and no-GC modes for explicit manual memory management. In GC mode, `Memory.free()` removes an object from the GC heap and frees it immediately. In no-GC mode, it maps directly to `free()`.
- **`gg.config` project configuration file** — stores project-level settings (`garbage_collector`, `memory_limit`). Automatically read during `build`/`run`/`emit-c` by searching up from the source file directory.
- **Runtime memory limit enforcement** — `gg_gc_set_memory_limit()` API in the C runtime. When memory limit is exceeded, a forced GC collection occurs; if still over limit, the program exits with code 137 and a descriptive error message.{message: "Invalid username or password."}
message
: 
"Invalid username or password."
- **Runtime no-GC conditional compilation** — when `GG_NO_GC` is defined, all GC functions become no-ops via macros, allocations use raw `calloc()`, and `free()` works directly. Zero GC overhead for manual memory mode.

### Changed

- **Object allocation in CodeGen** — `_create()` factory functions now use `gg_alloc()` instead of raw `malloc()+memset()`, ensuring proper GC tracking in GC mode and correct allocation in no-GC mode.
- **Build output** — now displays `[no-gc]` or `[mem: X MB]` suffix when memory configuration is active.
- **`Memory` recognized as built-in identifier** — no longer produces "undefined identifier" warnings in the semantic analyzer.

---

## [0.8.0] - 2026-02-21 01:42

### Added

- **Type mismatch detection in Semantic Analyzer** — the compiler now reports errors when assigning incompatible types to variables (e.g. `int a = "test"` → `type mismatch: cannot assign 'string' to variable 'a' of type 'int'`). Supports implicit numeric widening conversions (byte→int→long→float→double).
- **New `CheckTypeCompatibility` and `IsImplicitNumericConversion` methods** in SemanticAnalyzer for comprehensive type checking on variable declarations.
- **10 new unit tests** for type mismatch detection (string→int, char→int, bool→int, int→string, double→int) and valid implicit conversions (int→double, float→double, var inference).
- **4 new Lexer tests** for char literal validation (valid char, escaped char, multi-char error, empty char error).

### Changed

- **Improved char literal error messages** — `'teste'` now reports `character literal contains too many characters — Did you mean to use a string? Use double quotes instead: "teste"` instead of the confusing `unterminated character literal`.
- **Lexer handles empty char literals** — `''` now correctly reports `empty character literal` instead of being silently consumed.
- **Standardized diagnostic output format** — all errors now use `(line:col): message` format consistently across Lexer, Parser, and Semantic phases, replacing the old `at line L:C` format.
- **CLI error output** — `PrintPhaseErrors` now labels errors with `[Phase]` prefix for clarity (e.g. `[Lexer] (3:17): ...`).

### Fixed

- **`int a = "teste"` compiled silently** — now correctly reports a type mismatch error at the semantic analysis stage.
- **`int a = 't'` compiled and produced `116`** — now correctly reports a type mismatch error (char → int requires explicit cast).
- **`int a = 'teste'` produced confusing error** — now gives a clear, actionable error message suggesting double quotes.

---

## [0.7.0] - 2026-02-19

### Added

- **Package Manager CLI** (`gg pkg`) — full-featured package manager with subcommands: `init` (creates `gg.json` manifest), `install` (installs standard libraries to local `libs/`), `list`, `remove`, `update`, `search`, and `publish` (placeholder for future registry). Supports aliases (`add`, `rm`, `ls`, `upgrade`, `find`).
- **Windows MSYS2/MinGW enhanced support** — `NativeCompiler` now checks 12+ GCC paths on Windows (MSYS2 ucrt64/mingw64/clang64, MinGW-w64, TDM-GCC, WinLibs, Chocolatey, Scoop), supports `MSYS2_ROOT` environment variable, and provides detailed platform-specific installation instructions when GCC is not found via `CheckGccAvailability()`.
- **macOS Homebrew GCC paths** — `NativeCompiler` now checks Homebrew gcc-13/gcc-14 paths on Apple Silicon.

### Changed

- **Semantic analyzer error messages** — 8 messages improved with detailed descriptions and actionable suggestions (duplicate type/field/variable, undefined base class, parameter conflicts, type inference).
- **Parser error messages** — 6 messages improved with context and syntax hints (type declaration, class member, interface method, type reference, expression, expected token).
- **Lexer error messages** — 5 messages improved with detailed descriptions (unterminated string/char literals, empty char literal, unexpected characters with Unicode code point).
- **CLI error messages** — build/check errors now use ANSI colors (red for errors, green for success, cyan for info), include helpful hints with example commands, and show possible causes for GCC failures.
- **CLI unknown command handling** — uses Levenshtein distance to suggest similar valid commands ("Did you mean?").
- **`DiagnosticBag.ToString()`** — now uses ANSI color codes (red=error, yellow=warning, cyan=info) for terminal output.

### Fixed

- **Test assertions** — updated 5 semantic analyzer test assertions to match new improved error message wording.

---

## [0.6.0] - 2026-02-18 22:13

### Added

- **Test Suite Expansion** — 36 new tests (31 → 75 total)
  - `SemanticAnalyzerTests.cs` — 9 tests: duplicate type/field/param/variable detection, undefined base class, valid programs, class table population, inheritance chain validation.
  - `CodeGenTests.cs` — 10 tests: C headers, struct generation, constructors, vtables, static methods, printf output, inheritance chain with base fields.
  - `EndToEndTests.cs` — 7 tests: full pipeline (.gg → C → GCC → binary → stdout check). Tests for hello world, arithmetic, classes/inheritance, for/while loops, if/else, recursion (factorial), Math.abs. Graceful skip when GCC is unavailable.
  - `StandardLibraryTests.cs` — 10 tests: Math.abs/max/min/combined, Console I/O (string/int/bool), extension method codegen (string/int), semantic checks on builtins.

- **New Examples** — 5 new example programs (3 → 8 total)
  - `interfaces.gg` — `interface IShape` with `Circle` and `Rectangle` implementations.
  - `enums.gg` — `enum Color` and `enum Direction` with integer values and conditionals.
  - `libraries.gg` — `Math.abs()`, `Math.max()`, `Math.min()`, `Math.clamp()` with expressions.
  - `extensions.gg` — Extension methods on strings (`.trim()`, `.toUpper()`, `.contains()`) and numbers (`.abs()`, `.clamp()`), chained calls.
  - `imports.gg` — `module MyApp; import Math;` with a `Geometry` utility class.

- **CONTRIBUTING.md** — Contribution guidelines: prerequisites, project structure, development workflow, code style, how to add language features and standard libraries, issue reporting.

- **Compiler Architecture Documentation** (`docs/architecture.md`)
  - Full pipeline documentation: Lexer, Parser, Semantic Analyzer (3-pass), C Code Generator (vtables, structs, OOP), NativeCompiler (GCC wrapper).
  - Key types reference for each stage.
  - C runtime and standard library architecture overview.

- **CI/CD** (`.github/workflows/ci.yml`)
  - GitHub Actions workflow: checkout → .NET 10 setup → GCC install → restore → build → test → run examples.
  - Triggers on push to `main`/`develop` and pull requests to `main`.

- **MIT LICENSE** — Added `LICENSE` file (was declared in README but file was missing).

### Changed

- **Version bump** — `0.5.0` → `0.6.0` across all project files.
- **Makefile** — `make examples` target now runs all 8 examples (was 3).
- **.gitignore** — Comprehensive rewrite covering .NET artifacts, IDE files, OS files, and all example binaries.

### Removed

- **`ggLang.Core`** — Removed empty project (`src/ggLang.Core/`). Had only a `.csproj` with no source files. Removed from `ggLang.slnx`, `ggLang.Compiler.csproj`, and `ggLang.Tests.csproj`.

---

## [0.5.0] - 2026-02-18 12:00

### Added

- **Extension Methods on Primitive Types**
  - Added `.toString()`, `.toInt()`, `.toLong()`, `.toDouble()`, `.toFloat()`, `.toBool()`, `.toChar()` type conversion extensions for all applicable primitive types.
  - Added `.round(decimals)`, `.roundToInt()`, `.ceil()`, `.floor()` numeric extensions for `double` and `float`.
  - Added `.abs()` and `.clamp(min, max)` numeric extensions for `int`, `long`, `double`, `float`.
  - Added string extensions: `.length()`, `.isEmpty()`, `.toUpper()`, `.toLower()`, `.trim()`, `.substring(start, length)`, `.contains(sub)`, `.startsWith(prefix)`, `.endsWith(suffix)`, `.indexOf(sub)`, `.replace(old, new)`, `.charAt(index)`, `.reverse()`, `.padLeft(width, char)`, `.padRight(width, char)`.
  - Supports chained calls (e.g., `" hello ".trim().toUpper()`) and expression targets (e.g., `(10).toString()`).
  - Runtime implemented in C (`gg_ext_{type}_{method}`) with full codegen integration.

- **Standard Library Protection**
  - Standard library files (`.lib.gg`) are now read-only and cannot be compiled, checked, or emitted as C directly.
  - Protection applies to files in `libs/` directory (relative to project) and installed system paths (`/usr/local/lib/gglang/libs/`).
  - Users are shown a helpful message: "Standard libraries (.lib.gg) are read-only. Import them with: import LibName;"

- **Collections Library Expansion** (`Collections.lib.gg`)
  - Added `HashMap` — FNV-1a hashing, open addressing with linear probing, power-of-2 capacity, 0.75 load factor, tombstone deletion. O(1) amortized get/put/remove.
  - Added `HashSet` — Same high-performance hashing as HashMap, keys-only. O(1) amortized add/contains/remove.
  - Added `LinkedList` — Doubly-linked list with head/tail pointers. O(1) addFirst/addLast/removeFirst/removeLast, O(n) get/contains.
  - Added `Stack` — Dynamic array-backed LIFO with doubling strategy. O(1) amortized push/pop/peek.
  - Added `Queue` — Circular buffer-backed FIFO with auto-resize. O(1) amortized enqueue/dequeue/peek.
  - All collections include full C runtime implementations with memory management.

- **Base Utility Library** (`Base.lib.gg`)
  - Type checking utilities: `isInt()`, `isDecimal()`, `isBool()`, `isAlpha()`, `isDigit()`, `isAlphanumeric()`.
  - String utilities: `repeat()`, `join()`, `spaces()`, `isNullOrEmpty()`, `isNullOrWhitespace()`.
  - Math utilities: `sign()`, `isEven()`, `isOdd()`, `isPositive()`, `isNegative()`, `diff()`, `lerp()`, `map()`.
  - Comparison helpers: `maxInt()`, `minInt()`, `maxDecimal()`, `minDecimal()`.
  - Debug/logging: `debug()`, `warn()`, `error()`, `info()`.
  - Timing: `timestamp()`, `elapsed()`.

### Changed

- **Runtime Directory Resolution** (`NativeCompiler.FindRuntimeDir`)
  - Reordered search priority: development/workspace paths are now checked before installed system paths.
  - Prevents stale installed runtime from overshadowing newer workspace runtime during development.

---

## [0.4.0] - 2026-02-18 11:35

### Fixed

- **Code Generator — Crash when passing method calls as arguments to Console.write/writeLine**
  - Passing built-in method calls (e.g., `Math.abs(-333)`) directly as arguments to `Console.write()` or `Console.writeLine()` caused a segfault at runtime.
  - Root cause: `InferMethodCallReturnType` could not resolve return types for built-in classes (`Math`, `Console`) since they are not in `_classDeclarations`, defaulting to `"string"` and generating `printf("%s", abs(...))` instead of the correct integer format specifier.
  - Added `InferMathReturnType` and `InferConsoleReturnType` helper methods to correctly infer return types for all built-in Math and Console methods.

---

## [0.3.0] - 2026-02-18 10:04

### Added

- **Files standard library** (`libs/Files.lib.gg`)
  - `Files` class: `readAll`, `writeAll`, `append`, `exists`, `delete`, `copy`, `move`, `size`, `readLines`, `writeLines`, `create`
  - `Directory` class: `exists`, `create`, `remove`, `getCurrent`, `setCurrent`
  - `Path` class: `combine`, `getFileName`, `getExtension`, `getDirectory`
  - Full runtime implementation in C with cross-platform support

- **Crypto standard library** (`libs/Crypto.lib.gg`)
  - `Crypto` class: `sha256`, `md5`, `sha1`, `crc32`, `hmacSha256` — all return hex strings
  - `Base64` class: `encode`, `decode`
  - `Hex` class: `encode`, `decode`
  - `XorCipher` class: `encrypt`, `decrypt` — simple XOR cipher for educational use
  - `Random` class: `nextInt`, `nextString`, `uuid` (UUID v4)
  - Pure C implementations (no external crypto dependencies)

- **Network standard library** (`libs/Network.lib.gg`)
  - `Network` class: `resolve` (DNS), `ping` (TCP reachability), `getHostName`
  - `TcpClient` class: `connect`, `send`, `receive`, `close`, `isConnected`
  - `TcpServer` class: `start`, `accept`, `sendTo`, `receiveFrom`, `stop`, `isListening`
  - `UdpSocket` class: `bind`, `sendTo`, `receive`, `close`
  - `Url` class: `getProtocol`, `getHost`, `getPort`, `getPath`, `encode`, `decode`
  - Cross-platform: POSIX sockets (Linux/macOS) and Winsock2 (Windows)

- **OS standard library** (`libs/OS.lib.gg`)
  - `OS` class: `platform`, `arch`, `getEnv`, `setEnv`, `removeEnv`, `exit`, `time`, `sleep`, `cpuCount`, `userName`, `homeDir`, `tempDir`, `pathSeparator`, `lineEnding`
  - `Process` class: `exec` (capture stdout), `run` (exit code), `pid`
  - `Clock` class: `now` (high-res ms), `date`, `time`, `dateTime`
  - Full cross-platform implementations

- **Windows support** (basic)
  - Platform detection macros (`GG_PLATFORM_WINDOWS`, `GG_PLATFORM_LINUX`, `GG_PLATFORM_MACOS`)
  - Windows headers: `<windows.h>`, `<winsock2.h>`, `<ws2tcpip.h>`, `<io.h>`, `<direct.h>`
  - Cross-platform `#ifdef` guards throughout the entire runtime
  - Winsock2 initialization/shutdown in program lifecycle
  - GCC path detection for MSYS2/MinGW/TDM-GCC on Windows
  - Runtime directory resolution for `%LOCALAPPDATA%\gglang\` and `%ProgramFiles%\gglang\`
  - `-lws2_32` auto-linked on Windows builds

- **Runtime C implementations** for all new library functions
  - SHA-256, MD5, SHA-1 hash algorithms (pure C, no dependencies)
  - CRC32 checksum, HMAC-SHA256
  - Base64 and hex encoding/decoding
  - XOR cipher, random number/string/UUID generation
  - File I/O via `fopen`/`fread`/`fwrite`, `stat`, `mkdir`, `rename`, `remove`
  - Directory operations via platform-appropriate system calls
  - Path manipulation (combine, getFileName, getExtension, getDirectory)
  - Network: DNS resolution, TCP connectivity check, hostname retrieval
  - OS: environment variables, sleep, CPU count, username, home/temp dirs
  - Process: command execution via `popen`/`system`, PID retrieval
  - Clock: high-resolution monotonic time, date/time formatting

### Changed

- **Version bump** — `0.2.0` → `0.3.0` across all project files
- **Runtime** — expanded from ~560 lines to ~1100+ lines of C code
- **Runtime header** — added platform detection, conditional includes, 60+ new function declarations
- **NativeCompiler.cs** — Windows GCC path detection (MSYS2, MinGW, TDM-GCC), Winsock2 linking

---

## [0.3.0] - 2026-02-18 10:03

### Added

- **Garbage Collector** — Mark-and-sweep GC integrated into the runtime (`gg_runtime.h` / `gg_runtime.c`)
  - Automatic collection triggered when allocation count exceeds configurable threshold (default: 1024)
  - Conservative pointer scanning for reachable object discovery
  - Adaptive threshold that doubles when most objects survive a collection cycle
  - Root set management API (`gg_gc_add_root` / `gg_gc_remove_root`) with up to 4096 roots
  - GC statistics accessible via `gg_gc_get_state()`
  - GC object header (`gg_gc_header`) with intrusive linked list, size, and mark bit
  - Full lifecycle integration: `gg_gc_init()` at startup, `gg_gc_shutdown()` at exit

  ### Changed

- **Runtime `gg_alloc()`** now delegates to the GC allocator (`gg_gc_alloc`) instead of bare `calloc`
- **Runtime `gg_free()`** properly removes objects from GC tracking before freeing
- **Runtime `main()`** now calls `gg_gc_init()` before and `gg_gc_shutdown()` after `Program_main()`
- **`gg_retain()` / `gg_release()`** remain as no-ops for API compatibility

---

## [0.2.0] - 2026-02-18 00:12

### Added

- **Version bump** — `0.1.0` → `0.2.0` across all project files:
  - `src/ggLang.CLI/Program.cs` (compiler version constant)
  - `Makefile` and `build.sh`
- **README.md** — rewritten with full installation guide, CLI reference table, standard library listing, and updated project structure

---

## [0.1.0] - 2026-02-17

### Added

- **Compiler Core**
  - Lexer with full tokenization of ggLang syntax
  - Recursive descent parser producing complete AST
  - Semantic analyzer with 3-pass analysis (types → members → bodies)
  - C code generator with struct-based OOP, vtables, and virtual method dispatch
  - Native compilation via GCC backend

- **Language Features**
  - Classes with fields, methods, constructors
  - Inheritance with `: BaseClass` syntax and `base()` calls
  - Access modifiers: `public`, `private`, `protected`
  - Method modifiers: `static`, `virtual`, `override`
  - Primitive types: `int`, `float`, `double`, `bool`, `string`, `char`, `void`
  - Type inference with `var` keyword
  - Control flow: `if`/`else`, `while`, `for`, `return`
  - Operators: arithmetic, comparison, logical, assignment
  - String concatenation with `+` operator
  - `Console.writeLine()` / `Console.readLine()` built-ins
  - Annotations with `[@Name(args)]` syntax

- **CLI** (`gg` command)
  - `gg build <file.gg> [-o output]` — compile to native binary
  - `gg run <file.gg>` — build and run immediately
  - `gg check <file.gg>` — check for errors without compiling
  - `gg emit-c <file.gg> [-o out.c]` — emit generated C code
  - `gg init [project-name]` — scaffold a new ggLang project
  - `gg version` / `gg help`

- **Standard Library Infrastructure**
  - `libs/` directory with `.lib.gg` naming convention
  - `[@Library("Name", "Version")]` annotation for libraries
  - Initial libraries: Math, StringUtils, Collections

- **Build System**
  - Makefile with `make install` / `make uninstall`
  - `build.sh` shell script with colored output
  - Self-contained single-file binary (~71MB, linux-x64)
  - Runtime installed to `/usr/local/lib/gglang/runtime/`
  - Standard libs installed to `/usr/local/lib/gglang/libs/`
- **Runtime**
  - Minimal C runtime (`gg_runtime.h` / `gg_runtime.c`)
  - String operations, memory management, I/O wrappers

- **Testing**
  - 31 xUnit tests covering lexer and parser
  - 3 working examples (hello, classes, calculator)
