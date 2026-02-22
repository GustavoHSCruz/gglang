using System.Diagnostics;

namespace ggLang.Compiler.CodeGen;

/// <summary>
/// Wraps the native C compiler (GCC) to compile generated C code
/// into a native executable binary.
/// </summary>
public sealed class NativeCompiler
{
    private readonly string _gccPath;
    private readonly string _runtimeDir;

    public NativeCompiler()
    {
        _gccPath = FindGcc();
        _runtimeDir = FindRuntimeDir();
    }

    /// <summary>
    /// Compiles a C source file to a native binary.
    /// </summary>
    /// <param name="cSourcePath">Path to the generated .c file.</param>
    /// <param name="outputPath">Path for the output binary.</param>
    /// <param name="optimize">Whether to enable optimizations (-O2).</param>
    /// <returns>True if compilation succeeded.</returns>
    public (bool Success, string Output) Compile(string cSourcePath, string outputPath, bool optimize = false)
    {
        var runtimeC = Path.Combine(_runtimeDir, "gg_runtime.c");
        var runtimeH = _runtimeDir;

        var args = $"-o \"{outputPath}\" \"{cSourcePath}\"";

        // Include runtime if it exists
        if (File.Exists(runtimeC))
        {
            args += $" \"{runtimeC}\"";
        }

        args += $" -I\"{runtimeH}\"";
        args += " -lm"; // math library

        // Windows: link Winsock2 library
        if (OperatingSystem.IsWindows())
        {
            args += " -lws2_32";
        }
        else
        {
            args += " -pthread";
        }

        if (optimize)
            args += " -O2";
        else
            args += " -g"; // debug symbols

        args += " -Wno-incompatible-pointer-types";
        args += " -Wno-int-conversion";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _gccPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            var output = stdout + stderr;
            return (process.ExitCode == 0, output.Trim());
        }
        catch (Exception ex)
        {
            return (false, $"failed to run GCC: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs a compiled native binary.
    /// </summary>
    public (int ExitCode, string Output) Run(string binaryPath, string[]? args = null)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = args != null ? string.Join(" ", args) : "",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return (process.ExitCode, (stdout + stderr).TrimEnd());
        }
        catch (Exception ex)
        {
            return (-1, $"failed to run binary: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if GCC is available on the system.
    /// Returns a detailed message if not found.
    /// </summary>
    public static (bool Found, string Path, string? HelpMessage) CheckGccAvailability()
    {
        var gcc = FindGcc();
        
        // Try to actually run it
        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = gcc,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode == 0)
                return (true, gcc, null);
        }
        catch { /* GCC not executable */ }

        // GCC not found — provide helpful message based on OS
        string help;
        if (OperatingSystem.IsWindows())
        {
            help = """
                GCC was not found on your system. To install it on Windows:

                Option 1 — MSYS2 (recommended):
                  1. Download MSYS2 from https://www.msys2.org/
                  2. Open MSYS2 terminal and run:
                     pacman -S mingw-w64-ucrt-x86_64-gcc
                  3. Add C:\msys64\ucrt64\bin to your PATH

                Option 2 — MinGW-w64:
                  1. Download from https://winlibs.com/
                  2. Extract and add the bin/ folder to your PATH

                Option 3 — TDM-GCC:
                  1. Download from https://jmeubank.github.io/tdm-gcc/
                  2. Install and ensure it's in your PATH

                After installation, restart your terminal and try again.
                """;
        }
        else if (OperatingSystem.IsMacOS())
        {
            help = """
                GCC was not found on your system. To install it on macOS:
                  brew install gcc
                Or install Xcode command line tools:
                  xcode-select --install
                """;
        }
        else
        {
            help = """
                GCC was not found on your system. To install it:
                  Ubuntu/Debian:  sudo apt install gcc
                  Fedora:         sudo dnf install gcc
                  Arch:           sudo pacman -S gcc
                """;
        }

        return (false, gcc, help.TrimEnd());
    }

    private static string FindGcc()
    {
        // Try common paths (cross-platform)
        List<string> candidates;

        if (OperatingSystem.IsWindows())
        {
            candidates =
            [
                // MSYS2 environments (ucrt64 preferred — modern Universal CRT)
                @"C:\msys64\ucrt64\bin\gcc.exe",
                @"C:\msys64\mingw64\bin\gcc.exe",
                @"C:\msys64\clang64\bin\gcc.exe",
                @"C:\msys64\mingw32\bin\gcc.exe",
                // Standalone MinGW-w64 installations
                @"C:\mingw64\bin\gcc.exe",
                @"C:\mingw32\bin\gcc.exe",
                // TDM-GCC
                @"C:\TDM-GCC-64\bin\gcc.exe",
                @"C:\TDM-GCC-32\bin\gcc.exe",
                // WinLibs (common download locations)
                @"C:\Tools\mingw64\bin\gcc.exe",
                // Chocolatey / Scoop
                @"C:\ProgramData\chocolatey\bin\gcc.exe",
            ];

            // Also check user's scoop path
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add(Path.Combine(userProfile, @"scoop\apps\gcc\current\bin\gcc.exe"));

            // Check MSYS2 env variable
            var msys2Root = Environment.GetEnvironmentVariable("MSYS2_ROOT");
            if (!string.IsNullOrEmpty(msys2Root))
            {
                candidates.Insert(0, Path.Combine(msys2Root, @"ucrt64\bin\gcc.exe"));
                candidates.Insert(1, Path.Combine(msys2Root, @"mingw64\bin\gcc.exe"));
            }

            candidates.Add("gcc.exe");
            candidates.Add("gcc");
        }
        else
        {
            candidates =
            [
                "/usr/bin/gcc",
                "/usr/local/bin/gcc",
                // Homebrew on Apple Silicon
                "/opt/homebrew/bin/gcc-14",
                "/opt/homebrew/bin/gcc-13",
                "/opt/homebrew/bin/gcc",
                "gcc",
            ];
        }

        foreach (var path in candidates)
        {
            if (path is "gcc" or "gcc.exe" || File.Exists(path))
                return path;
        }

        return "gcc"; // fallback — will fail with helpful error
    }

    private static string FindRuntimeDir()
    {
        // 1. Check GG_RUNTIME_DIR environment variable
        var envDir = Environment.GetEnvironmentVariable("GG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(envDir) && Directory.Exists(envDir))
            return envDir;

        // 2. Relative to the assembly (dotnet run / development) — prioritized for dev workflow
        var assemblyDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidate = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", "..", "runtime"));
        if (Directory.Exists(candidate)) return candidate;

        // 3. Working directory/runtime
        candidate = Path.GetFullPath("runtime");
        if (Directory.Exists(candidate)) return candidate;

        // 4. Walk up parent dirs from working directory
        var dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 5; i++)
        {
            candidate = Path.Combine(dir, "runtime");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }

        // 5. Relative to the executable (self-contained publish)
        var exeDir = AppContext.BaseDirectory;
        candidate = Path.Combine(exeDir, "runtime");
        if (Directory.Exists(candidate)) return candidate;

        // 6. Installed location (platform-specific) — last resort
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var winDir = Path.Combine(appData, "gglang", "runtime");
            if (Directory.Exists(winDir)) return winDir;

            // Also try Program Files
            var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfDir = Path.Combine(progFiles, "gglang", "runtime");
            if (Directory.Exists(pfDir)) return pfDir;
        }
        else
        {
            var installedDir = "/usr/local/lib/gglang/runtime";
            if (Directory.Exists(installedDir)) return installedDir;
        }

        return "runtime";
    }
}
