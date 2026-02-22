using ggLang.Compiler.Lexer;
using ggLang.Compiler.Parser;
using ggLang.Compiler.Analysis;
using ggLang.Compiler.CodeGen;

namespace ggLang.CLI;

/// <summary>
/// Command-line interface for the ggLang compiler.
/// Commands: build, run, check, emit-c, init, version, help
/// </summary>
public class Program
{
    private const string Version = "0.6.0-beta";
    private const string Language = "ggLang";

    /// <summary>
    /// Known standard library directories (system-installed and project-local).
    /// Files inside these paths or with .lib.gg extension are read-only.
    /// </summary>
    private static readonly string[] StdLibPaths =
    [
        "/usr/local/lib/gglang/libs",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gglang", "libs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "gglang", "libs"),
    ];

    /// <summary>
    /// Checks whether a file is a protected standard library.
    /// Protected files cannot be compiled as entry-point targets.
    /// </summary>
    private static bool IsProtectedStdLib(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);

        // Any .lib.gg file inside a known system libs directory is protected
        foreach (var libDir in StdLibPaths)
        {
            if (fullPath.StartsWith(libDir, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Also protect the project-level libs/ directory
        var dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 5; i++)
        {
            var candidate = Path.Combine(dir, "libs");
            if (Directory.Exists(candidate) && fullPath.StartsWith(Path.GetFullPath(candidate), StringComparison.OrdinalIgnoreCase))
            {
                // Only protect .lib.gg files (standard libs), not user .gg files
                if (fullPath.EndsWith(".lib.gg", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            dir = Path.GetDirectoryName(dir) ?? dir;
        }

        return false;
    }

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();

        return command switch
        {
            "build" => Build(args.Skip(1).ToArray()),
            "run" => Run(args.Skip(1).ToArray()),
            "check" => Check(args.Skip(1).ToArray()),
            "emit-c" => EmitC(args.Skip(1).ToArray()),
            "init" => Init(args.Skip(1).ToArray()),
            "pkg" => PackageManager(args.Skip(1).ToArray()),
            "version" or "--version" or "-v" => PrintVersion(),
            "help" or "--help" or "-h" => PrintHelp(),
            _ => UnknownCommand(command)
        };
    }

    // ====================
    // COMMANDS
    // ====================

    /// <summary>
    /// Builds a .gg source file into a native binary.
    /// </summary>
    private static int Build(string[] args)
    {
        if (args.Length == 0)
        {
            PrintBuildError("no input file specified",
                "Provide a .gg source file to compile.",
                "gg build <file.gg> [-o output]");
            return 1;
        }

        var sourceFile = args[0];
        var outputFile = ParseOption(args, "-o")
            ?? Path.GetFileNameWithoutExtension(sourceFile);

        if (!File.Exists(sourceFile))
        {
            PrintBuildError($"file not found: {sourceFile}",
                "The specified source file does not exist. Check the file path and try again.",
                $"gg build {Path.GetFileName(sourceFile)}");
            return 1;
        }

        // Protect standard libraries from direct compilation
        if (IsProtectedStdLib(sourceFile))
        {
            PrintBuildError($"'{Path.GetFileName(sourceFile)}' is a protected standard library",
                "Standard libraries (.lib.gg) are read-only and cannot be compiled as entry points.\n" +
                "  To use a library, import it in your source file:",
                $"import {Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(sourceFile))};");
            return 1;
        }

        Console.WriteLine($"\u001b[36m[{Language}]\u001b[0m compiling {sourceFile}...");

        // Read source
        var source = File.ReadAllText(sourceFile);

        // Lex
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();
        if (lexer.HasErrors)
        {
            PrintPhaseErrors("Lexer", sourceFile, lexer.Errors);
            return 1;
        }

        // Parse
        var parser = new GgParser(tokens);
        var ast = parser.ParseCompilationUnit();
        if (parser.HasErrors)
        {
            PrintPhaseErrors("Parser", sourceFile, parser.Errors);
            return 1;
        }

        // Semantic analysis
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        if (analyzer.HasErrors)
        {
            Console.Error.WriteLine($"\n\u001b[31merror\u001b[0m: semantic analysis failed for {sourceFile}\n");
            foreach (var diag in analyzer.Diagnostics.GetSorted())
                Console.Error.WriteLine($"{diag}");
            var errCount = analyzer.Diagnostics.GetErrors().Count();
            Console.Error.WriteLine($"\n  {errCount} error(s) found. Fix the errors above and try again.\n");
            return 1;
        }

        // Generate C code
        var (memLimit, noGc) = ReadConfigFromFile(sourceFile);
        var codeGen = new CCodeGenerator(analyzer, memLimit, noGc);
        var cCode = codeGen.Generate(ast);

        // Write C code to temp file
        var cFile = Path.ChangeExtension(sourceFile, ".c");
        File.WriteAllText(cFile, cCode);

        // Compile with GCC
        var compiler = new NativeCompiler();
        var (success, output) = compiler.Compile(cFile, outputFile);

        if (!success)
        {
            Console.Error.WriteLine($"\n\u001b[31merror\u001b[0m: native compilation failed (GCC)\n");
            Console.Error.WriteLine($"  {output}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("  Possible causes:");
            Console.Error.WriteLine("    - GCC is not installed or not in PATH");
            Console.Error.WriteLine("    - The generated C code has incompatible constructs");
            Console.Error.WriteLine("    - Missing system libraries (try: sudo apt install build-essential)");
            Console.Error.WriteLine();
            return 1;
        }

        // Clean up C file
        try { File.Delete(cFile); } catch { }

        var size = new FileInfo(outputFile).Length;
        var memInfo = noGc ? " [no-gc]" : memLimit > 0 ? $" [mem: {FormatMemorySize(memLimit)}]" : "";
        Console.WriteLine($"\u001b[32m[{Language}]\u001b[0m build successful: {outputFile} ({size / 1024.0:F1} KB){memInfo}");
        return 0;
    }

    /// <summary>
    /// Builds and immediately runs a .gg source file.
    /// </summary>
    private static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("error: no input file specified");
            Console.Error.WriteLine("usage: gg run <file.gg>");
            return 1;
        }

        var sourceFile = args[0];
        var tempOutput = Path.Combine(Path.GetTempPath(), $"gg_{Path.GetFileNameWithoutExtension(sourceFile)}");

        // Build first
        var buildArgs = new[] { sourceFile, "-o", tempOutput };
        var buildResult = Build(buildArgs);
        if (buildResult != 0) return buildResult;

        Console.WriteLine();

        // Run
        var compiler = new NativeCompiler();
        var (exitCode, output) = compiler.Run(tempOutput);
        if (!string.IsNullOrEmpty(output))
            Console.WriteLine(output);

        // Clean up
        try { File.Delete(tempOutput); } catch { }

        return exitCode;
    }

    /// <summary>
    /// Checks a .gg file for errors without producing output.
    /// </summary>
    private static int Check(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("error: no input file specified");
            Console.Error.WriteLine("usage: gg check <file.gg>");
            return 1;
        }

        var sourceFile = args[0];
        if (!File.Exists(sourceFile))
        {
            Console.Error.WriteLine($"error: file not found: {sourceFile}");
            return 1;
        }

        if (IsProtectedStdLib(sourceFile))
        {
            Console.Error.WriteLine($"error: '{Path.GetFileName(sourceFile)}' is a standard library and cannot be modified.");
            Console.Error.WriteLine("Standard libraries (.lib.gg) are read-only. Import them with: import LibName;");
            return 1;
        }

        var source = File.ReadAllText(sourceFile);

        // Lex
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();
        if (lexer.HasErrors)
        {
            PrintErrors("Lexer", lexer.Errors);
            return 1;
        }

        // Parse
        var parser = new GgParser(tokens);
        var ast = parser.ParseCompilationUnit();
        if (parser.HasErrors)
        {
            PrintErrors("Parser", parser.Errors);
            return 1;
        }

        // Analyze
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);

        var errorCount = analyzer.Diagnostics.GetErrors().Count();
        var warnCount = analyzer.Diagnostics.Diagnostics
            .Count(d => d.Severity == DiagnosticSeverity.Warning);

        if (analyzer.HasErrors)
        {
            foreach (var diag in analyzer.Diagnostics.GetSorted())
                Console.Error.WriteLine($"  {diag}");
            Console.Error.WriteLine($"\n{errorCount} error(s), {warnCount} warning(s)");
            return 1;
        }

        Console.WriteLine($"[{Language}] {sourceFile}: OK ({warnCount} warning(s))");
        if (warnCount > 0)
        {
            foreach (var diag in analyzer.Diagnostics.GetSorted())
                Console.WriteLine($"  {diag}");
        }

        return 0;
    }

    /// <summary>
    /// Emits the generated C code to stdout or a file.
    /// </summary>
    private static int EmitC(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("error: no input file specified");
            Console.Error.WriteLine("usage: gg emit-c <file.gg> [-o output.c]");
            return 1;
        }

        var sourceFile = args[0];
        var outputFile = ParseOption(args, "-o");

        if (!File.Exists(sourceFile))
        {
            Console.Error.WriteLine($"error: file not found: {sourceFile}");
            return 1;
        }

        if (IsProtectedStdLib(sourceFile))
        {
            Console.Error.WriteLine($"error: '{Path.GetFileName(sourceFile)}' is a standard library and cannot be modified.");
            Console.Error.WriteLine("Standard libraries (.lib.gg) are read-only. Import them with: import LibName;");
            return 1;
        }

        var source = File.ReadAllText(sourceFile);

        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();
        if (lexer.HasErrors) { PrintErrors("Lexer", lexer.Errors); return 1; }

        var parser = new GgParser(tokens);
        var ast = parser.ParseCompilationUnit();
        if (parser.HasErrors) { PrintErrors("Parser", parser.Errors); return 1; }

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);

        var (memLimit, noGc) = ReadConfigFromFile(sourceFile);
        var codeGen = new CCodeGenerator(analyzer, memLimit, noGc);
        var cCode = codeGen.Generate(ast);

        if (outputFile != null)
        {
            File.WriteAllText(outputFile, cCode);
            Console.WriteLine($"[{Language}] C code written to {outputFile}");
        }
        else
        {
            Console.Write(cCode);
        }

        return 0;
    }

    // ====================
    // HELPERS
    // ====================

    /// <summary>
    /// Initializes a new ggLang project in the current directory.
    /// Usage: gg init [project-name] [--mem <size>] [--no-gc]
    /// </summary>
    private static int Init(string[] args)
    {
        // Parse --mem and --no-gc options before extracting project name
        string? memoryLimit = null;
        bool noGc = false;
        var filteredArgs = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--mem" && i + 1 < args.Length)
            {
                memoryLimit = args[i + 1];
                if (!ValidateMemoryLimit(memoryLimit))
                {
                    Console.Error.WriteLine($"\u001b[31merror\u001b[0m: invalid memory limit '{memoryLimit}'");
                    Console.Error.WriteLine("  Expected format: <number><unit> where unit is B, K, KB, M, MB, G, or GB");
                    Console.Error.WriteLine("  Examples: gg init --mem 512M, gg init --mem 2G, gg init my_project --mem 256MB");
                    return 1;
                }
                i++; // skip the value
            }
            else if (args[i] is "--no-gc" or "--no-garbage-collector")
            {
                noGc = true;
            }
            else
            {
                filteredArgs.Add(args[i]);
            }
        }

        // --no-gc and --mem are mutually exclusive
        if (noGc && memoryLimit != null)
        {
            Console.Error.WriteLine($"\u001b[31merror\u001b[0m: '--no-gc' and '--mem' cannot be used together");
            Console.Error.WriteLine("  When the garbage collector is disabled, memory is managed manually.");
            Console.Error.WriteLine("  The --mem limit only applies when the GC is active.");
            return 1;
        }

        var projectName = filteredArgs.Count > 0 ? filteredArgs[0] : Path.GetFileName(Directory.GetCurrentDirectory());

        // Sanitize project name
        projectName = string.Concat(projectName.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'));
        if (string.IsNullOrWhiteSpace(projectName))
            projectName = "my_project";

        var projectDir = filteredArgs.Count > 0
            ? Path.Combine(Directory.GetCurrentDirectory(), projectName)
            : Directory.GetCurrentDirectory();

        // If creating in a subdirectory, make sure it doesn't already exist with .gg files
        if (filteredArgs.Count > 0)
        {
            if (Directory.Exists(projectDir) && Directory.GetFiles(projectDir, "*.gg").Length > 0)
            {
                Console.Error.WriteLine($"error: directory '{projectName}' already contains .gg files");
                return 1;
            }
        }

        Console.WriteLine($"[{Language}] initializing project '{projectName}'...");
        Console.WriteLine();

        // Create directories
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(Path.Combine(projectDir, "src"));
        Directory.CreateDirectory(Path.Combine(projectDir, "libs"));

        // Create main source file
        var mainFile = Path.Combine(projectDir, "src", "main.gg");
        if (!File.Exists(mainFile))
        {
            File.WriteAllText(mainFile, $@"// {projectName} - Main entry point
// Created with ggLang v{Version}

class Program {{
    static void main() {{
        Console.writeLine(""Hello from {projectName}!"");
    }}
}}
");
            Console.WriteLine($"  created  src/main.gg");
        }
        else
        {
            Console.WriteLine($"  skipped  src/main.gg (already exists)");
        }

        // Create README
        var readmeFile = Path.Combine(projectDir, "README.md");
        if (!File.Exists(readmeFile))
        {
            File.WriteAllText(readmeFile, $@"# {projectName}

A project written in [ggLang](https://github.com/ggLang/ggLang).

## Build & Run

```bash
gg build src/main.gg -o {projectName}
gg run src/main.gg
```

## Project Structure

```
{projectName}/
├── src/
│   └── main.gg        # Entry point
├── libs/               # Libraries (.lib.gg)
└── README.md
```
");
            Console.WriteLine($"  created  README.md");
        }
        else
        {
            Console.WriteLine($"  skipped  README.md (already exists)");
        }

        // Create .gitignore
        var gitignoreFile = Path.Combine(projectDir, ".gitignore");
        if (!File.Exists(gitignoreFile))
        {
            File.WriteAllText(gitignoreFile, @"# ggLang build artifacts
*.c
*.o
build/
out/

# OS
.DS_Store
Thumbs.db
");
            Console.WriteLine($"  created  .gitignore");
        }
        else
        {
            Console.WriteLine($"  skipped  .gitignore (already exists)");
        }

        Console.WriteLine();
        Console.WriteLine($"[{Language}] project '{projectName}' initialized!");
        Console.WriteLine();

        // Create gg.config if --mem or --no-gc was specified
        if (memoryLimit != null || noGc)
        {
            var configFile = Path.Combine(projectDir, "gg.config");
            var configContent = $@"# ggLang Project Configuration
# Generated by gg init v{Version}

# Garbage collector mode.
# Options: enabled (default), disabled
# When disabled, the user must manage memory manually using Memory.free().
# This is intended for embedded systems or performance-critical applications.
garbage_collector = {(noGc ? "disabled" : "enabled")}

# Maximum memory limit for the application (only when GC is enabled).
# When exceeded, the GC will be forced to collect. If memory is still
# above the limit after collection, the application will terminate.
# Formats: <number><unit> where unit is B, K/KB, M/MB, G/GB
# Set to 0 to disable the memory limit.
memory_limit = {memoryLimit ?? "0"}
";
            File.WriteAllText(configFile, configContent);

            if (noGc)
            {
                Console.WriteLine($"  created  gg.config (GC: disabled, manual memory management)");
            }
            else
            {
                var memBytes = ParseMemoryLimit(memoryLimit!);
                Console.WriteLine($"  created  gg.config (memory limit: {FormatMemorySize(memBytes)})");
            }
            Console.WriteLine();
        }

        if (filteredArgs.Count > 0)
            Console.WriteLine($"  cd {projectName}");

        Console.WriteLine($"  gg run src/main.gg");
        Console.WriteLine();

        return 0;
    }

    private static int PrintVersion()
    {
        Console.WriteLine($"{Language} compiler v{Version}");
        Console.WriteLine("Target: native binary via C transpilation (GCC)");
        Console.WriteLine("Runtime: GCC backend");
        return 0;
    }

    // ====================
    // MEMORY LIMIT HELPERS
    // ====================

    /// <summary>
    /// Validates a memory limit string (e.g., "512M", "2G", "256MB", "1024K").
    /// </summary>
    private static bool ValidateMemoryLimit(string limit)
    {
        return ParseMemoryLimit(limit) > 0;
    }

    /// <summary>
    /// Parses a memory limit string into bytes.
    /// Supports: B, K, KB, M, MB, G, GB (case-insensitive).
    /// Returns 0 if invalid.
    /// </summary>
    private static long ParseMemoryLimit(string limit)
    {
        if (string.IsNullOrWhiteSpace(limit)) return 0;

        limit = limit.Trim();

        // Find where the number ends and the unit begins
        int unitStart = 0;
        for (int i = 0; i < limit.Length; i++)
        {
            if (!char.IsDigit(limit[i]) && limit[i] != '.')
            {
                unitStart = i;
                break;
            }
        }

        if (unitStart == 0) return 0; // No number part

        var numberPart = limit[..unitStart];
        var unitPart = limit[unitStart..].Trim().ToUpperInvariant();

        if (!double.TryParse(numberPart, System.Globalization.CultureInfo.InvariantCulture, out var number) || number <= 0)
            return 0;

        long multiplier = unitPart switch
        {
            "B" => 1L,
            "K" or "KB" => 1024L,
            "M" or "MB" => 1024L * 1024,
            "G" or "GB" => 1024L * 1024 * 1024,
            _ => 0
        };

        if (multiplier == 0) return 0;

        return (long)(number * multiplier);
    }

    /// <summary>
    /// Formats a byte count into a human-readable string.
    /// </summary>
    private static string FormatMemorySize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024L)
            return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    /// <summary>
    /// Reads the gg.config file and returns (memoryLimit, noGc).
    /// Searches up from the source file directory.
    /// </summary>
    private static (long MemoryLimit, bool NoGc) ReadConfigFromFile(string sourceFilePath)
    {
        long memoryLimit = 0;
        bool noGc = false;

        var dir = Path.GetDirectoryName(Path.GetFullPath(sourceFilePath));
        for (int i = 0; i < 10 && dir != null; i++)
        {
            var configPath = Path.Combine(dir, "gg.config");
            if (File.Exists(configPath))
            {
                foreach (var line in File.ReadAllLines(configPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith('#') || string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    var parts = trimmed.Split('=', 2);
                    if (parts.Length != 2) continue;

                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    switch (key)
                    {
                        case "memory_limit":
                            memoryLimit = ParseMemoryLimit(value);
                            break;
                        case "garbage_collector":
                            noGc = value.Equals("disabled", StringComparison.OrdinalIgnoreCase);
                            break;
                    }
                }
                return (memoryLimit, noGc);
            }
            dir = Path.GetDirectoryName(dir);
        }
        return (0, false);
    }

    private static int PrintHelp()
    {
        Console.WriteLine($@"
{Language} Compiler v{Version}
================================

Usage: gg <command> [options]

Commands:
  build <file.gg> [-o output]   Compile to native binary
  run <file.gg>                 Build and run immediately
  check <file.gg>               Check for errors
  emit-c <file.gg> [-o out.c]   Emit generated C code
  init [name] [--mem] [--no-gc] Initialize a new ggLang project
  pkg <subcommand>              Package manager
  version                       Show version
  help                          Show this help

Package Manager (gg pkg):
  pkg init                      Initialize gg.json manifest
  pkg install <name>            Install a package from registry
  pkg list                      List installed packages
  pkg remove <name>             Remove a package
  pkg update                    Update all packages
  pkg search <query>            Search the package registry
  pkg publish                   Publish current package

Examples:
  gg build hello.gg
  gg build hello.gg -o myapp
  gg run hello.gg
  gg check hello.gg
  gg emit-c hello.gg -o hello.c
  gg init my_project
  gg init --mem 512M
  gg init my_project --mem 2G
  gg init --no-gc
  gg pkg init
  gg pkg install Math
");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"\u001b[31merror\u001b[0m: unknown command '{command}'");
        Console.Error.WriteLine();

        // Suggest closest match
        var commands = new[] { "build", "run", "check", "emit-c", "init", "pkg", "version", "help" };
        var suggestions = commands
            .Select(c => (cmd: c, dist: LevenshteinDistance(command, c)))
            .Where(x => x.dist <= 3)
            .OrderBy(x => x.dist)
            .Take(2)
            .ToList();

        if (suggestions.Count > 0)
        {
            Console.Error.WriteLine("  Did you mean?");
            foreach (var s in suggestions)
                Console.Error.WriteLine($"    gg {s.cmd}");
            Console.Error.WriteLine();
        }

        Console.Error.WriteLine("  Run 'gg help' for a list of available commands.");
        return 1;
    }

    private static void PrintErrors(string phase, IEnumerable<string> errors)
    {
        foreach (var error in errors)
            Console.Error.WriteLine($"  [{phase}] {error}");
    }

    private static void PrintBuildError(string error, string hint, string example)
    {
        Console.Error.WriteLine($"\n\u001b[31merror\u001b[0m: {error}");
        Console.Error.WriteLine($"  {hint}");
        Console.Error.WriteLine($"\n  Example: {example}\n");
    }

    private static void PrintPhaseErrors(string phase, string file, IEnumerable<string> errors)
    {
        Console.Error.WriteLine($"\n\u001b[31merror\u001b[0m: {phase.ToLower()} errors in {file}\n");
        foreach (var error in errors)
            Console.Error.WriteLine($"  [\u001b[31m{phase}\u001b[0m] {error}");
        Console.Error.WriteLine($"\n  Fix the errors above and try again.\n");
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    // ====================
    // PACKAGE MANAGER
    // ====================

    private const string ManifestFile = "gg.json";

    /// <summary>
    /// Package manager for ggLang libraries.
    /// Subcommands: init, install, list, remove, update, search, publish
    /// </summary>
    private static int PackageManager(string[] args)
    {
        if (args.Length == 0)
        {
            PrintBuildError("no subcommand specified",
                "The package manager requires a subcommand.",
                "gg pkg init | install <name> | list | remove <name> | update | search <query>");
            return 1;
        }

        var subcommand = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();

        return subcommand switch
        {
            "init" => PkgInit(subArgs),
            "install" or "add" => PkgInstall(subArgs),
            "list" or "ls" => PkgList(),
            "remove" or "rm" or "uninstall" => PkgRemove(subArgs),
            "update" or "upgrade" => PkgUpdate(),
            "search" or "find" => PkgSearch(subArgs),
            "publish" => PkgPublish(),
            _ => PkgUnknownCommand(subcommand)
        };
    }

    private static int PkgInit(string[] args)
    {
        if (File.Exists(ManifestFile))
        {
            Console.Error.WriteLine($"\u001b[33mwarning\u001b[0m: {ManifestFile} already exists in this directory.");
            return 1;
        }

        var projectName = args.Length > 0
            ? args[0]
            : Path.GetFileName(Directory.GetCurrentDirectory());

        var manifest = $@"{{
  ""name"": ""{projectName}"",
  ""version"": ""0.1.0"",
  ""description"": """",
  ""author"": """",
  ""license"": ""MIT"",
  ""main"": ""src/main.gg"",
  ""dependencies"": {{}},
  ""devDependencies"": {{}}
}}
";
        File.WriteAllText(ManifestFile, manifest);
        Console.WriteLine($"\u001b[32m[pkg]\u001b[0m created {ManifestFile}");
        Console.WriteLine();
        Console.WriteLine($"  Project: {projectName}");
        Console.WriteLine($"  Version: 0.1.0");
        Console.WriteLine();
        Console.WriteLine("  Next steps:");
        Console.WriteLine("    gg pkg install <package>   Add a dependency");
        Console.WriteLine("    gg build src/main.gg       Build your project");
        Console.WriteLine();
        return 0;
    }

    private static int PkgInstall(string[] args)
    {
        if (args.Length == 0)
        {
            // Install all dependencies from gg.json
            if (!File.Exists(ManifestFile))
            {
                PrintBuildError("no gg.json found",
                    "Run 'gg pkg init' first to create a package manifest.",
                    "gg pkg init");
                return 1;
            }

            Console.WriteLine($"\u001b[36m[pkg]\u001b[0m installing dependencies from {ManifestFile}...");
            // Read manifest and install from standard libs
            InstallStandardLibs();
            Console.WriteLine($"\u001b[32m[pkg]\u001b[0m all dependencies installed.");
            return 0;
        }

        var packageName = args[0];
        var version = args.Length > 1 ? args[1] : "latest";

        Console.WriteLine($"\u001b[36m[pkg]\u001b[0m installing {packageName}@{version}...");

        // Check if it's a standard library
        var libsDir = FindLibsDir();
        if (libsDir != null)
        {
            var libFile = Path.Combine(libsDir, $"{packageName}.lib.gg");
            if (File.Exists(libFile))
            {
                // Copy to local libs/
                var localLibs = Path.Combine(Directory.GetCurrentDirectory(), "libs");
                Directory.CreateDirectory(localLibs);
                var dest = Path.Combine(localLibs, $"{packageName}.lib.gg");
                File.Copy(libFile, dest, overwrite: true);

                // Update manifest if exists
                UpdateManifestDependency(packageName, version);

                Console.WriteLine($"\u001b[32m[pkg]\u001b[0m installed {packageName} (standard library)");
                Console.WriteLine($"  → libs/{packageName}.lib.gg");
                Console.WriteLine();
                Console.WriteLine($"  Usage: import {packageName};");
                Console.WriteLine();
                return 0;
            }
        }

        // Check package registry (placeholder for future registry)
        Console.Error.WriteLine($"\u001b[33mwarning\u001b[0m: package '{packageName}' not found in the registry.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  Available standard libraries:");

        var availableLibs = GetAvailableLibs();
        foreach (var lib in availableLibs)
            Console.Error.WriteLine($"    - {lib}");

        Console.Error.WriteLine();
        Console.Error.WriteLine("  The ggLang package registry is coming soon.");
        Console.Error.WriteLine("  For now, install standard libraries or add .lib.gg files manually.");
        Console.Error.WriteLine();
        return 1;
    }

    private static int PkgList()
    {
        var localLibs = Path.Combine(Directory.GetCurrentDirectory(), "libs");

        Console.WriteLine($"\u001b[36m[pkg]\u001b[0m installed packages:\n");

        if (Directory.Exists(localLibs))
        {
            var libs = Directory.GetFiles(localLibs, "*.lib.gg");
            if (libs.Length == 0)
            {
                Console.WriteLine("  (no packages installed)");
            }
            else
            {
                foreach (var lib in libs)
                {
                    var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(lib));
                    var size = new FileInfo(lib).Length;
                    Console.WriteLine($"  {name,-20} {size / 1024.0:F1} KB   libs/{Path.GetFileName(lib)}");
                }
            }
        }
        else
        {
            Console.WriteLine("  (no libs/ directory found)");
        }

        Console.WriteLine();

        // Show manifest info
        if (File.Exists(ManifestFile))
        {
            Console.WriteLine($"  Manifest: {ManifestFile}");
        }
        else
        {
            Console.WriteLine($"  No {ManifestFile} found. Run 'gg pkg init' to create one.");
        }

        Console.WriteLine();
        return 0;
    }

    private static int PkgRemove(string[] args)
    {
        if (args.Length == 0)
        {
            PrintBuildError("no package name specified",
                "Provide the name of the package to remove.",
                "gg pkg remove <name>");
            return 1;
        }

        var packageName = args[0];
        var localLibs = Path.Combine(Directory.GetCurrentDirectory(), "libs");
        var libFile = Path.Combine(localLibs, $"{packageName}.lib.gg");

        if (!File.Exists(libFile))
        {
            Console.Error.WriteLine($"\u001b[33mwarning\u001b[0m: package '{packageName}' is not installed.");
            return 1;
        }

        File.Delete(libFile);
        Console.WriteLine($"\u001b[32m[pkg]\u001b[0m removed {packageName}");
        Console.WriteLine($"  Deleted libs/{packageName}.lib.gg");
        Console.WriteLine();
        return 0;
    }

    private static int PkgUpdate()
    {
        Console.WriteLine($"\u001b[36m[pkg]\u001b[0m checking for updates...");

        var localLibs = Path.Combine(Directory.GetCurrentDirectory(), "libs");
        var libsDir = FindLibsDir();

        if (!Directory.Exists(localLibs) || libsDir == null)
        {
            Console.WriteLine("  No packages to update.");
            return 0;
        }

        var updated = 0;
        foreach (var localLib in Directory.GetFiles(localLibs, "*.lib.gg"))
        {
            var name = Path.GetFileName(localLib);
            var sourceLib = Path.Combine(libsDir, name);

            if (File.Exists(sourceLib))
            {
                var localMod = File.GetLastWriteTime(localLib);
                var sourceMod = File.GetLastWriteTime(sourceLib);

                if (sourceMod > localMod)
                {
                    File.Copy(sourceLib, localLib, overwrite: true);
                    var libName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(name));
                    Console.WriteLine($"  \u001b[32m↑\u001b[0m updated {libName}");
                    updated++;
                }
            }
        }

        if (updated == 0)
            Console.WriteLine("  All packages are up to date.");
        else
            Console.WriteLine($"\n  {updated} package(s) updated.");

        Console.WriteLine();
        return 0;
    }

    private static int PkgSearch(string[] args)
    {
        var query = args.Length > 0 ? args[0].ToLowerInvariant() : "";

        Console.WriteLine($"\u001b[36m[pkg]\u001b[0m available packages:\n");

        var availableLibs = GetAvailableLibs();
        var results = string.IsNullOrEmpty(query)
            ? availableLibs
            : availableLibs.Where(l => l.ToLowerInvariant().Contains(query)).ToList();

        if (results.Count == 0)
        {
            Console.WriteLine($"  No packages matching '{query}'.");
        }
        else
        {
            foreach (var lib in results)
                Console.WriteLine($"  {lib,-20} standard library");
        }

        Console.WriteLine();
        Console.WriteLine("  The community package registry is coming soon!");
        Console.WriteLine("  Install with: gg pkg install <name>");
        Console.WriteLine();
        return 0;
    }

    private static int PkgPublish()
    {
        if (!File.Exists(ManifestFile))
        {
            PrintBuildError("no gg.json found",
                "Create a package manifest first.",
                "gg pkg init");
            return 1;
        }

        Console.WriteLine($"\u001b[33m[pkg]\u001b[0m the ggLang package registry is not yet available.");
        Console.WriteLine();
        Console.WriteLine("  Package publishing will be available when the registry launches.");
        Console.WriteLine("  In the meantime, you can share packages via GitHub repositories.");
        Console.WriteLine();
        Console.WriteLine("  To prepare your package for publishing:");
        Console.WriteLine("    1. Ensure gg.json has correct name, version, and description");
        Console.WriteLine("    2. Add [@Library] annotation to your main class");
        Console.WriteLine("    3. Include documentation and examples");
        Console.WriteLine();
        return 0;
    }

    private static int PkgUnknownCommand(string subcommand)
    {
        Console.Error.WriteLine($"\u001b[31merror\u001b[0m: unknown pkg subcommand '{subcommand}'");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  Available subcommands:");
        Console.Error.WriteLine("    init, install, list, remove, update, search, publish");
        Console.Error.WriteLine();
        return 1;
    }

    // ====================
    // PACKAGE HELPERS
    // ====================

    private static string? FindLibsDir()
    {
        // Check standard locations
        var candidates = new[]
        {
            "/usr/local/lib/gglang/libs",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gglang", "libs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "gglang", "libs"),
        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir)) return dir;
        }

        // Walk up from current directory
        var currentDir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 5; i++)
        {
            var candidate = Path.Combine(currentDir, "libs");
            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.lib.gg").Length > 0)
                return candidate;
            currentDir = Path.GetDirectoryName(currentDir) ?? currentDir;
        }

        return null;
    }

    private static List<string> GetAvailableLibs()
    {
        var libs = new List<string>();
        var libsDir = FindLibsDir();
        if (libsDir != null)
        {
            foreach (var file in Directory.GetFiles(libsDir, "*.lib.gg"))
            {
                libs.Add(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file)));
            }
        }
        return libs.OrderBy(l => l).ToList();
    }

    private static void InstallStandardLibs()
    {
        var libsDir = FindLibsDir();
        if (libsDir == null)
        {
            Console.Error.WriteLine("  \u001b[33mwarning\u001b[0m: no standard library directory found.");
            return;
        }

        var localLibs = Path.Combine(Directory.GetCurrentDirectory(), "libs");
        Directory.CreateDirectory(localLibs);

        foreach (var lib in Directory.GetFiles(libsDir, "*.lib.gg"))
        {
            var dest = Path.Combine(localLibs, Path.GetFileName(lib));
            File.Copy(lib, dest, overwrite: true);
            var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(lib));
            Console.WriteLine($"  \u001b[32m+\u001b[0m {name}");
        }
    }

    private static void UpdateManifestDependency(string packageName, string version)
    {
        if (!File.Exists(ManifestFile)) return;

        try
        {
            var content = File.ReadAllText(ManifestFile);
            // Simple JSON manipulation — add dependency if not present
            if (!content.Contains($"\"{packageName}\""))
            {
                var depMarker = "\"dependencies\": {";
                var idx = content.IndexOf(depMarker, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var insertIdx = idx + depMarker.Length;
                    var existing = content[(insertIdx)..].TrimStart();
                    var comma = existing.StartsWith("}") ? "" : ",";
                    var versionStr = version == "latest" ? "*" : version;
                    var entry = $"\n    \"{packageName}\": \"{versionStr}\"{comma}";
                    content = content[..insertIdx] + entry + content[insertIdx..];
                    File.WriteAllText(ManifestFile, content);
                }
            }
        }
        catch { /* Best effort */ }
    }

    private static string? ParseOption(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == flag)
                return args[i + 1];
        }
        return null;
    }
}
