using ggLang.Compiler.Lexer;
using ggLang.Compiler.Parser;
using ggLang.Compiler.Analysis;
using ggLang.Compiler.CodeGen;

namespace ggLang.Tests;

/// <summary>
/// Unit tests for the ggLang C code generator.
/// Verifies that generated C code contains expected constructs.
/// </summary>
public class CodeGenTests
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

    // ==========================================
    // HEADER & INCLUDES
    // ==========================================

    [Fact]
    public void GeneratedCode_ContainsHeaders()
    {
        var code = GenerateC("class Empty { }");

        Assert.Contains("#include <stdio.h>", code);
        Assert.Contains("#include <stdlib.h>", code);
        Assert.Contains("#include <string.h>", code);
        Assert.Contains("#include <stdbool.h>", code);
        Assert.Contains("#include \"gg_runtime.h\"", code);
    }

    // ==========================================
    // CLASS STRUCT GENERATION
    // ==========================================

    [Fact]
    public void GeneratedCode_ContainsClassStruct()
    {
        var code = GenerateC(@"
            class Person {
                string name;
                int age;
            }
        ");

        Assert.Contains("struct Person {", code);
        Assert.Contains("Person_VTable* _vtable;", code);
    }

    [Fact]
    public void GeneratedCode_ContainsForwardDeclarations()
    {
        var code = GenerateC(@"
            class Foo { }
            class Bar { }
        ");

        Assert.Contains("typedef struct Foo Foo;", code);
        Assert.Contains("typedef struct Bar Bar;", code);
        Assert.Contains("typedef struct Foo_VTable Foo_VTable;", code);
    }

    // ==========================================
    // CONSTRUCTOR & FACTORY
    // ==========================================

    [Fact]
    public void GeneratedCode_ContainsConstructor()
    {
        var code = GenerateC(@"
            class Person {
                string name;

                Person(string name) {
                    this.name = name;
                }
            }
        ");

        Assert.Contains("void Person_construct(Person* self", code);
        Assert.Contains("Person* Person_create(", code);
    }

    [Fact]
    public void Constructor_SetsVTable()
    {
        var code = GenerateC(@"
            class Foo {
                Foo() { }
            }
        ");

        Assert.Contains("self->_vtable = &Foo_vtable_instance;", code);
    }

    // ==========================================
    // METHOD GENERATION
    // ==========================================

    [Fact]
    public void GeneratedCode_ContainsMethod()
    {
        var code = GenerateC(@"
            class Calculator {
                int add(int a, int b) {
                    return a + b;
                }
            }
        ");

        Assert.Contains("int Calculator_add(Calculator* self, int a, int b)", code);
        Assert.Contains("return (a + b);", code);
    }

    [Fact]
    public void StaticMethod_NoSelfParameter()
    {
        var code = GenerateC(@"
            class App {
                static void main() {
                    Console.writeLine(""hello"");
                }
            }
        ");

        // Static methods should not have a self parameter
        Assert.Contains("void App_main()", code);
        Assert.DoesNotContain("App_main(App* self)", code);
    }

    // ==========================================
    // VTABLE GENERATION
    // ==========================================

    [Fact]
    public void GeneratedCode_ContainsVTable()
    {
        var code = GenerateC(@"
            class Animal {
                virtual string speak() {
                    return ""..."";
                }
            }
        ");

        Assert.Contains("struct Animal_VTable {", code);
        Assert.Contains("Animal_VTable Animal_vtable_instance", code);
        Assert.Contains(".speak = Animal_speak", code);
    }

    // ==========================================
    // CONSOLE OUTPUT
    // ==========================================

    [Fact]
    public void GeneratedCode_ContainsPrintf()
    {
        var code = GenerateC(@"
            class Program {
                static void main() {
                    Console.writeLine(""Hello, World!"");
                }
            }
        ");

        Assert.Contains("printf", code);
        Assert.Contains("Hello, World!", code);
    }

    // ==========================================
    // INHERITANCE
    // ==========================================

    [Fact]
    public void Inheritance_CallsBaseConstructor()
    {
        var code = GenerateC(@"
            class Animal {
                string name;

                Animal(string name) {
                    this.name = name;
                }
            }

            class Dog : Animal {
                Dog(string name) : base(name) { }
            }
        ");

        Assert.Contains("Dog_construct(Dog* self", code);
        Assert.Contains("Animal_construct((Animal*)self", code);
    }

    [Fact]
    public void Inheritance_DogStructHasBaseFields()
    {
        var code = GenerateC(@"
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

        // Dog struct should contain both 'name' (inherited) and 'breed'
        var dogStructStart = code.IndexOf("struct Dog {");
        Assert.True(dogStructStart >= 0, "Dog struct not found");
        var dogStructEnd = code.IndexOf("};", dogStructStart);
        var dogStruct = code.Substring(dogStructStart, dogStructEnd - dogStructStart);

        Assert.Contains("name", dogStruct);
        Assert.Contains("breed", dogStruct);
    }
}
