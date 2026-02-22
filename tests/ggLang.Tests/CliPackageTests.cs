using ggLang.CLI;

namespace ggLang.Tests;

public sealed class CliPackageTests : IDisposable
{
    private static readonly object CliLock = new();
    private readonly string _tempDir;
    private readonly string _projectDir;
    private readonly string _stdlibDir;

    public CliPackageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gglang_cli_pkg_{Guid.NewGuid():N}");
        _projectDir = Path.Combine(_tempDir, "project");
        _stdlibDir = Path.Combine(_tempDir, "stdlib");
        Directory.CreateDirectory(_projectDir);
        Directory.CreateDirectory(_stdlibDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void PkgInstall_LocksLocalLibraryAsReadOnly()
    {
        var sourceLib = Path.Combine(_stdlibDir, "Math.lib.gg");
        File.WriteAllText(sourceLib, "class MathLib { }");

        var code = RunCli(_projectDir, _stdlibDir, "pkg", "install", "Math");
        Assert.Equal(0, code);

        var installedLib = Path.Combine(_projectDir, "libs", "Math.lib.gg");
        Assert.True(File.Exists(installedLib));
        Assert.True(IsReadOnly(installedLib));
    }

    [Fact]
    public void PkgUpdate_UpdatesLockedLibraryAndRelocks()
    {
        var sourceLib = Path.Combine(_stdlibDir, "Math.lib.gg");
        File.WriteAllText(sourceLib, "class MathLib { static int version = 1; }");
        Assert.Equal(0, RunCli(_projectDir, _stdlibDir, "pkg", "install", "Math"));

        File.WriteAllText(sourceLib, "class MathLib { static int version = 2; }");
        File.SetLastWriteTimeUtc(sourceLib, DateTime.UtcNow.AddMinutes(1));

        var updateCode = RunCli(_projectDir, _stdlibDir, "pkg", "update");
        Assert.Equal(0, updateCode);

        var installedLib = Path.Combine(_projectDir, "libs", "Math.lib.gg");
        var content = File.ReadAllText(installedLib);
        Assert.Contains("version = 2", content);
        Assert.True(IsReadOnly(installedLib));
    }

    [Fact]
    public void PkgRemove_RemovesLockedLibrary()
    {
        var sourceLib = Path.Combine(_stdlibDir, "Math.lib.gg");
        File.WriteAllText(sourceLib, "class MathLib { }");
        Assert.Equal(0, RunCli(_projectDir, _stdlibDir, "pkg", "install", "Math"));

        var removeCode = RunCli(_projectDir, _stdlibDir, "pkg", "remove", "Math");
        Assert.Equal(0, removeCode);

        var installedLib = Path.Combine(_projectDir, "libs", "Math.lib.gg");
        Assert.False(File.Exists(installedLib));
    }

    private static int RunCli(string projectDir, string stdlibDir, params string[] args)
    {
        lock (CliLock)
        {
            var originalDir = Directory.GetCurrentDirectory();
            var originalStdLib = Environment.GetEnvironmentVariable("GG_STDLIB_DIR");
            try
            {
                Directory.SetCurrentDirectory(projectDir);
                Environment.SetEnvironmentVariable("GG_STDLIB_DIR", stdlibDir);
                return Program.Main(args);
            }
            finally
            {
                Environment.SetEnvironmentVariable("GG_STDLIB_DIR", originalStdLib);
                Directory.SetCurrentDirectory(originalDir);
            }
        }
    }

    private static bool IsReadOnly(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.ReadOnly) != 0;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & UnixFileMode.UserWrite) == 0;
        }
        catch
        {
            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.ReadOnly) != 0;
        }
    }
}
