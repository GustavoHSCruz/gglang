using ggLang.Compiler.Lexer;
using ggLang.Compiler.Parser;
using ggLang.Compiler.Analysis;
using ggLang.Compiler.CodeGen;

namespace ggLang.Tests;

/// <summary>
/// End-to-end integration tests.
/// Compiles .gg source → C → native binary → runs → checks stdout.
/// Tests are skipped if GCC is not available.
/// </summary>
public class EndToEndTests : IDisposable
{
    private readonly string _tempDir;
    private readonly bool _gccAvailable;

    public EndToEndTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gglang_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _gccAvailable = IsGccAvailable();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* cleanup best-effort */ }
    }

    private static bool IsGccAvailable()
    {
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "gcc",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private (int ExitCode, string Output) CompileAndRun(string source, long memoryLimit = 0, bool noGc = false)
    {
        // Lex
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();
        if (lexer.HasErrors)
            return (-1, $"Lexer errors: {string.Join(", ", lexer.Errors)}");

        // Parse
        var parser = new GgParser(tokens);
        var unit = parser.ParseCompilationUnit();
        if (parser.HasErrors)
            return (-1, $"Parser errors: {string.Join(", ", parser.Errors)}");

        // Analyze
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(unit);

        // Generate C
        var codegen = new CCodeGenerator(analyzer, memoryLimit, noGc);
        var cCode = codegen.Generate(unit);

        // Write C file
        var cPath = Path.Combine(_tempDir, "test.c");
        var binPath = Path.Combine(_tempDir, "test");
        File.WriteAllText(cPath, cCode);

        // Compile and run
        var compiler = new NativeCompiler();
        var (success, compileOutput) = compiler.Compile(cPath, binPath);
        if (!success)
            return (-1, $"GCC compilation failed: {compileOutput}");

        return compiler.Run(binPath);
    }

    // ==========================================
    // HELLO WORLD
    // ==========================================

    [Fact]
    public void HelloWorld_PrintsExpectedOutput()
    {
        if (!_gccAvailable)
        {
            // Skip test — GCC not available
            return;
        }

        var (exitCode, output) = CompileAndRun(@"
            class Program {
                static void main() {
                    Console.writeLine(""Hello, World!"");
                }
            }
        ");

        Assert.Equal(0, exitCode);
        Assert.Contains("Hello, World!", output);
    }

    // ==========================================
    // ARITHMETIC
    // ==========================================

    [Fact]
    public void Arithmetic_CorrectResults()
    {
        if (!_gccAvailable) return;

        var (exitCode, output) = CompileAndRun(@"
            class Program {
                static void main() {
                    int sum = 10 + 20;
                    Console.writeLine(sum);

                    int diff = 50 - 15;
                    Console.writeLine(diff);

                    int prod = 6 * 7;
                    Console.writeLine(prod);
                }
            }
        ");

        Assert.Equal(0, exitCode);
        Assert.Contains("30", output);
        Assert.Contains("35", output);
        Assert.Contains("42", output);
    }

    // ==========================================
    // CLASSES & INHERITANCE
    // ==========================================

    [Fact]
    public void ClassesAndInheritance_CorrectOutput()
    {
        if (!_gccAvailable) return;

        var (exitCode, output) = CompileAndRun(@"
            class Animal {
                string name;

                Animal(string name) {
                    this.name = name;
                }

                virtual string speak() {
                    return ""..."";
                }
            }

            class Dog : Animal {
                Dog(string name) : base(name) { }

                override string speak() {
                    return ""Woof!"";
                }
            }

            class Cat : Animal {
                Cat(string name) : base(name) { }

                override string speak() {
                    return ""Meow!"";
                }
            }

            class Program {
                static void main() {
                    var dog = new Dog(""Rex"");
                    var cat = new Cat(""Whiskers"");
                    Console.writeLine(dog.speak());
                    Console.writeLine(cat.speak());
                }
            }
        ");

        Assert.Equal(0, exitCode);
        Assert.Contains("Woof!", output);
        Assert.Contains("Meow!", output);
    }

    // ==========================================
    // CONTROL FLOW
    // ==========================================

    [Fact]
    public void ForLoop_CorrectOutput()
    {
        if (!_gccAvailable) return;

        var (exitCode, output) = CompileAndRun(@"
            class Program {
                static void main() {
                    int sum = 0;
                    for (int i = 1; i <= 5; i++) {
                        sum = sum + i;
                    }
                    Console.writeLine(sum);
                }
            }
        ");

        Assert.Equal(0, exitCode);
        Assert.Contains("15", output);
    }

    [Fact]
    public void WhileLoop_CorrectOutput()
    {
        if (!_gccAvailable) return;

        var (exitCode, output) = CompileAndRun(@"
            class Program {
                static void main() {
                    int x = 10;
                    while (x > 0) {
                        x = x - 3;
                    }
                    Console.writeLine(x);
                }
            }
        ");

        Assert.Equal(0, exitCode);
        Assert.Contains("-2", output);
    }

    [Fact]
    public void IfElse_CorrectBranch()
    {
        if (!_gccAvailable) return;

        var (exitCode, output) = CompileAndRun(@"
            class Program {
                static void main() {
                    int x = 42;
                    if (x > 100) {
                        Console.writeLine(""big"");
                    } else {
                        Console.writeLine(""small"");
                    }
                }
            }
        ");

        Assert.Equal(0, exitCode);
        Assert.Contains("small", output);
        Assert.DoesNotContain("big", output);
    }

    // ==========================================
    // METHOD CALLS
    // ==========================================

    [Fact]
    public void MethodCalls_ReturnValues()
    {
        if (!_gccAvailable) return;

        var (exitCode, output) = CompileAndRun(@"
            class Calculator {
                int factorial(int n) {
                    if (n <= 1) {
                        return 1;
                    }
                    return n * this.factorial(n - 1);
                }
            }

            class Program {
                static void main() {
                    var calc = new Calculator();
                    int result = calc.factorial(5);
                    Console.writeLine(result);
                }
            }
        ");

        Assert.Equal(0, exitCode);
        Assert.Contains("120", output);
    }

    // ==========================================
    // MATH BUILT-IN
    // ==========================================

    [Fact]
    public void MathAbs_CorrectOutput()
    {
        if (!_gccAvailable) return;

        var (exitCode, output) = CompileAndRun(@"
            class Program {
                static void main() {
                    int result = Math.abs(-42);
                    Console.writeLine(result);
                }
            }
        ");

        Assert.Equal(0, exitCode);
        Assert.Contains("42", output);
    }

    [Fact]
    public void GcRoots_LocalReferenceSurvivesHeavyAllocations()
    {
        if (!_gccAvailable) return;

        var (exitCode, output) = CompileAndRun(@"
            class Box {
                int value;
                Box(int value) {
                    this.value = value;
                }
            }

            class Program {
                static void main() {
                    Box last = new Box(777);
                    for (int i = 0; i < 6000; i++) {
                        Box tmp = new Box(i);
                    }
                    Console.writeLine(last.value);
                }
            }
        ");

        Assert.Equal(0, exitCode);
        Assert.Contains("777", output);
    }

    [Fact]
    public void MemoryLimit_ExitsWith137WhenExceeded()
    {
        if (!_gccAvailable) return;

        var (exitCode, output) = CompileAndRun(@"
            class Box {
                int value;
                Box(int value) {
                    this.value = value;
                }
            }

            class Program {
                static void main() {
                    Box box = new Box(1);
                    Console.writeLine(box.value);
                }
            }
        ", memoryLimit: 1);

        Assert.Equal(137, exitCode);
        Assert.Contains("memory limit exceeded", output, StringComparison.OrdinalIgnoreCase);
    }
}
