using ggLang.Compiler.Lexer;
using ggLang.Compiler.Parser;
using ggLang.Compiler.Analysis;

namespace ggLang.Tests;

/// <summary>
/// Unit tests for the ggLang semantic analyzer.
/// Tests error detection: duplicate types, undefined bases, scoping, etc.
/// </summary>
public class SemanticAnalyzerTests
{
    private SemanticAnalyzer Analyze(string source)
    {
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();
        Assert.False(lexer.HasErrors, $"Lexer errors: {string.Join(", ", lexer.Errors)}");

        var parser = new GgParser(tokens);
        var unit = parser.ParseCompilationUnit();
        Assert.False(parser.HasErrors, $"Parser errors: {string.Join(", ", parser.Errors)}");

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(unit);
        return analyzer;
    }

    // ==========================================
    // VALID PROGRAMS — no errors expected
    // ==========================================

    [Fact]
    public void ValidSimpleClass_NoErrors()
    {
        var analyzer = Analyze(@"
            class Hello {
                int x;

                Hello() {
                    this.x = 0;
                }

                int getX() {
                    return this.x;
                }
            }
        ");

        Assert.False(analyzer.HasErrors);
    }

    [Fact]
    public void ValidInheritance_NoErrors()
    {
        var analyzer = Analyze(@"
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
        ");

        Assert.False(analyzer.HasErrors);
    }

    [Fact]
    public void ValidInterface_NoErrors()
    {
        var analyzer = Analyze(@"
            interface IShape {
                double area();
                string describe();
            }

            class Program {
                static void main() {
                    int x = 1;
                }
            }
        ");

        Assert.False(analyzer.HasErrors);
    }

    [Fact]
    public void InterfaceImplementation_AcceptsOrWarns()
    {
        // When a class uses : InterfaceName, the analyzer may report
        // a warning because interfaces aren't in the class table.
        // This is expected behavior — it shouldn't crash.
        var analyzer = Analyze(@"
            interface IShape {
                double area();
            }

            class Circle : IShape {
                double radius;

                Circle(double radius) {
                    this.radius = radius;
                }

                double area() {
                    return 3.14159 * this.radius * this.radius;
                }
            }
        ");

        // The analyzer completes without crashing
        Assert.NotNull(analyzer.ClassTable);
        Assert.True(analyzer.ClassTable.ContainsKey("Circle"));
    }

    [Fact]
    public void ValidEnum_NoErrors()
    {
        var analyzer = Analyze(@"
            enum Color {
                Red = 0,
                Green = 1,
                Blue = 2
            }

            class Program {
                static void main() {
                    int c = 1;
                }
            }
        ");

        Assert.False(analyzer.HasErrors);
    }

    [Fact]
    public void ValidVarWithInitializer_NoErrors()
    {
        var analyzer = Analyze(@"
            class Test {
                void foo() {
                    var x = 42;
                    var name = ""hello"";
                }
            }
        ");

        Assert.False(analyzer.HasErrors);
    }

    // ==========================================
    // ERROR DETECTION
    // ==========================================

    [Fact]
    public void DuplicateTypeName_ReportsError()
    {
        var analyzer = Analyze(@"
            class Foo { }
            class Foo { }
        ");

        Assert.True(analyzer.HasErrors);
        Assert.Contains(analyzer.Diagnostics.Diagnostics,
            d => d.Message.Contains("already declared") && d.Message.Contains("Foo"));
    }

    [Fact]
    public void UndefinedBaseClass_ReportsError()
    {
        var analyzer = Analyze(@"
            class Dog : Animal { }
        ");

        Assert.True(analyzer.HasErrors);
        Assert.Contains(analyzer.Diagnostics.Diagnostics,
            d => d.Message.Contains("base class") && d.Message.Contains("Animal"));
    }

    [Fact]
    public void DuplicateField_ReportsError()
    {
        var analyzer = Analyze(@"
            class Foo {
                int x;
                int x;
            }
        ");

        Assert.True(analyzer.HasErrors);
        Assert.Contains(analyzer.Diagnostics.Diagnostics,
            d => d.Message.Contains("already declared") && d.Message.Contains("x"));
    }

    [Fact]
    public void DuplicateParameter_ReportsError()
    {
        var analyzer = Analyze(@"
            class Foo {
                void bar(int a, int a) { }
            }
        ");

        Assert.True(analyzer.HasErrors);
        Assert.Contains(analyzer.Diagnostics.Diagnostics,
            d => d.Message.Contains("already declared") && d.Message.Contains("a"));
    }

    [Fact]
    public void VarWithoutInitializer_ReportsError()
    {
        var analyzer = Analyze(@"
            class Foo {
                void bar() {
                    var x;
                }
            }
        ");

        Assert.True(analyzer.HasErrors);
        Assert.Contains(analyzer.Diagnostics.Diagnostics,
            d => d.Message.Contains("cannot infer type"));
    }

    [Fact]
    public void DuplicateVariable_ReportsError()
    {
        var analyzer = Analyze(@"
            class Foo {
                void bar() {
                    int x = 1;
                    int x = 2;
                }
            }
        ");

        Assert.True(analyzer.HasErrors);
        Assert.Contains(analyzer.Diagnostics.Diagnostics,
            d => d.Message.Contains("already declared") && d.Message.Contains("x"));
    }

    // ==========================================
    // CLASS TABLE POPULATION
    // ==========================================

    [Fact]
    public void ClassTable_ContainsRegisteredClass()
    {
        var analyzer = Analyze(@"
            class Person {
                string name;
                int age;

                Person(string name, int age) {
                    this.name = name;
                    this.age = age;
                }
            }
        ");

        Assert.True(analyzer.ClassTable.ContainsKey("Person"));
        var classInfo = analyzer.ClassTable["Person"];
        Assert.True(classInfo.Fields.ContainsKey("name"));
        Assert.True(classInfo.Fields.ContainsKey("age"));
        Assert.True(classInfo.HasConstructor);
    }

    [Fact]
    public void ClassTable_InheritsBaseMembers()
    {
        var analyzer = Analyze(@"
            class Animal {
                string name;

                Animal(string name) {
                    this.name = name;
                }
            }

            class Dog : Animal {
                string breed;

                Dog(string name, string breed) : base(name) {
                    this.breed = breed;
                }
            }
        ");

        Assert.False(analyzer.HasErrors);
        var dogInfo = analyzer.ClassTable["Dog"];
        // Dog should inherit 'name' from Animal
        Assert.True(dogInfo.Fields.ContainsKey("name"));
        Assert.True(dogInfo.Fields.ContainsKey("breed"));
    }

    // ==========================================
    // TYPE MISMATCH DETECTION
    // ==========================================

    [Fact]
    public void StringToInt_ReportsTypeMismatch()
    {
        var analyzer = Analyze(@"
            class Program {
                static void main() {
                    int a = ""teste"";
                }
            }
        ");

        Assert.True(analyzer.HasErrors);
        Assert.Contains(analyzer.Diagnostics.Diagnostics,
            d => d.Message.Contains("type mismatch") && d.Message.Contains("string") && d.Message.Contains("int"));
    }

    [Fact]
    public void CharToInt_ReportsTypeMismatch()
    {
        var analyzer = Analyze(@"
            class Program {
                static void main() {
                    int a = 't';
                }
            }
        ");

        Assert.True(analyzer.HasErrors);
        Assert.Contains(analyzer.Diagnostics.Diagnostics,
            d => d.Message.Contains("type mismatch") && d.Message.Contains("char") && d.Message.Contains("int"));
    }

    [Fact]
    public void BoolToInt_ReportsTypeMismatch()
    {
        var analyzer = Analyze(@"
            class Program {
                static void main() {
                    int x = true;
                }
            }
        ");

        Assert.True(analyzer.HasErrors);
        Assert.Contains(analyzer.Diagnostics.Diagnostics,
            d => d.Message.Contains("type mismatch") && d.Message.Contains("bool") && d.Message.Contains("int"));
    }

    [Fact]
    public void IntToString_ReportsTypeMismatch()
    {
        var analyzer = Analyze(@"
            class Program {
                static void main() {
                    string s = 42;
                }
            }
        ");

        Assert.True(analyzer.HasErrors);
        Assert.Contains(analyzer.Diagnostics.Diagnostics,
            d => d.Message.Contains("type mismatch") && d.Message.Contains("int") && d.Message.Contains("string"));
    }

    [Fact]
    public void IntToInt_NoError()
    {
        var analyzer = Analyze(@"
            class Program {
                static void main() {
                    int x = 42;
                }
            }
        ");

        Assert.False(analyzer.HasErrors);
    }

    [Fact]
    public void StringToString_NoError()
    {
        var analyzer = Analyze(@"
            class Program {
                static void main() {
                    string s = ""hello"";
                }
            }
        ");

        Assert.False(analyzer.HasErrors);
    }

    [Fact]
    public void IntToDouble_ImplicitConversion_NoError()
    {
        var analyzer = Analyze(@"
            class Program {
                static void main() {
                    double d = 42;
                }
            }
        ");

        Assert.False(analyzer.HasErrors);
    }

    [Fact]
    public void FloatToDouble_ImplicitConversion_NoError()
    {
        var analyzer = Analyze(@"
            class Program {
                static void main() {
                    double d = 3.14;
                }
            }
        ");

        Assert.False(analyzer.HasErrors);
    }

    [Fact]
    public void DoubleToInt_ReportsTypeMismatch()
    {
        var analyzer = Analyze(@"
            class Program {
                static void main() {
                    int x = 3.14;
                }
            }
        ");

        Assert.True(analyzer.HasErrors);
        Assert.Contains(analyzer.Diagnostics.Diagnostics,
            d => d.Message.Contains("type mismatch") && d.Message.Contains("double") && d.Message.Contains("int"));
    }

    [Fact]
    public void VarInference_NoTypeMismatch()
    {
        var analyzer = Analyze(@"
            class Program {
                static void main() {
                    var x = 42;
                    var s = ""hello"";
                    var b = true;
                }
            }
        ");

        Assert.False(analyzer.HasErrors);
    }
}
