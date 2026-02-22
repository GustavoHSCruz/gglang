using ggLang.Compiler.Lexer;
using ggLang.Compiler.Parser;
using ggLang.Compiler.Analysis;
using ggLang.Compiler.CodeGen;

namespace ggLang.Tests;

/// <summary>
/// Tests that standard library features compile and (where possible) run correctly.
/// Verifies the compiler pipeline handles built-in classes (Math, Console).
/// </summary>
public class StandardLibraryTests
{
    private string GenerateC(string source)
    {
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();
        Assert.False(lexer.HasErrors, $"Lexer errors: {string.Join(", ", lexer.Errors)}");

        var parser = new GgParser(tokens);
        var unit = parser.ParseCompilationUnit();
        Assert.False(parser.HasErrors, $"Parser errors: {string.Join(", ", parser.Errors)}");

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(unit);

        var codegen = new CCodeGenerator(analyzer);
        return codegen.Generate(unit);
    }

    private SemanticAnalyzer Analyze(string source)
    {
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();
        Assert.False(lexer.HasErrors);

        var parser = new GgParser(tokens);
        var unit = parser.ParseCompilationUnit();
        Assert.False(parser.HasErrors);

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(unit);
        return analyzer;
    }

    // ==========================================
    // MATH LIBRARY
    // ==========================================

    [Fact]
    public void MathAbs_CompilesSuccessfully()
    {
        var code = GenerateC(@"
            class Program {
                static void main() {
                    int result = Math.abs(-10);
                    Console.writeLine(result);
                }
            }
        ");

        Assert.Contains("abs(", code);
        Assert.Contains("printf", code);
    }

    [Fact]
    public void MathMax_CompilesSuccessfully()
    {
        var code = GenerateC(@"
            class Program {
                static void main() {
                    int result = Math.max(5, 10);
                    Console.writeLine(result);
                }
            }
        ");

        // Should generate some form of max call
        Assert.Contains("printf", code);
    }

    [Fact]
    public void MathMin_CompilesSuccessfully()
    {
        var code = GenerateC(@"
            class Program {
                static void main() {
                    int result = Math.min(5, 10);
                    Console.writeLine(result);
                }
            }
        ");

        Assert.Contains("printf", code);
    }

    [Fact]
    public void MathCombined_CompilesSuccessfully()
    {
        var code = GenerateC(@"
            class Program {
                static void main() {
                    int a = Math.abs(-5);
                    int b = Math.max(a, 10);
                    Console.writeLine(b);
                }
            }
        ");

        Assert.Contains("abs(", code);
        Assert.Contains("printf", code);
    }

    // ==========================================
    // CONSOLE I/O
    // ==========================================

    [Fact]
    public void ConsoleWriteLine_StringLiteral()
    {
        var code = GenerateC(@"
            class Program {
                static void main() {
                    Console.writeLine(""test output"");
                }
            }
        ");

        Assert.Contains("printf", code);
        Assert.Contains("test output", code);
    }

    [Fact]
    public void ConsoleWriteLine_IntegerVariable()
    {
        var code = GenerateC(@"
            class Program {
                static void main() {
                    int x = 42;
                    Console.writeLine(x);
                }
            }
        ");

        Assert.Contains("printf", code);
        // The codegen uses %lld with (long long) cast for integer formatting
        Assert.Contains("%lld", code);
    }

    [Fact]
    public void ConsoleWriteLine_BooleanLiteral()
    {
        var code = GenerateC(@"
            class Program {
                static void main() {
                    Console.writeLine(true);
                    Console.writeLine(false);
                }
            }
        ");

        Assert.Contains("printf", code);
    }

    // ==========================================
    // SEMANTIC CHECKS ON BUILTINS
    // ==========================================

    [Fact]
    public void MathCall_NoSemanticErrors()
    {
        var analyzer = Analyze(@"
            class Program {
                static void main() {
                    int x = Math.abs(-5);
                    int y = Math.max(3, 7);
                    Console.writeLine(x);
                    Console.writeLine(y);
                }
            }
        ");

        // Math and Console are built-in — should not produce errors
        Assert.False(analyzer.HasErrors);
    }

    [Fact]
    public void ConsoleCall_NoSemanticErrors()
    {
        var analyzer = Analyze(@"
            class Program {
                static void main() {
                    Console.writeLine(""Hello"");
                    Console.write(""World"");
                }
            }
        ");

        Assert.False(analyzer.HasErrors);
    }

    // ==========================================
    // EXTENSION METHODS (CODEGEN)
    // ==========================================

    [Fact]
    public void StringExtension_GeneratesExtensionCall()
    {
        var code = GenerateC(@"
            class Program {
                static void main() {
                    string name = ""hello"";
                    Console.writeLine(name.toUpper());
                }
            }
        ");

        // Extension methods map to gg_ext_{type}_{method}
        Assert.Contains("gg_ext_", code);
    }

    [Fact]
    public void IntExtension_GeneratesExtensionCall()
    {
        var code = GenerateC(@"
            class Program {
                static void main() {
                    int x = -42;
                    Console.writeLine(x.abs());
                }
            }
        ");

        Assert.Contains("gg_ext_", code);
    }
}
