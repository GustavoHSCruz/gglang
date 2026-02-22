using ggLang.Compiler.Lexer;
using ggLang.Compiler.Parser;
using ggLang.Compiler.Parser.Ast;

namespace ggLang.Tests;

/// <summary>
/// Unit tests for the ggLang parser with C#-style syntax.
/// </summary>
public class ParserTests
{
    private CompilationUnit Parse(string source)
    {
        var lexer = new GgLexer(source);
        var tokens = lexer.Tokenize();
        Assert.False(lexer.HasErrors, $"Lexer errors: {string.Join(", ", lexer.Errors)}");

        var parser = new GgParser(tokens);
        var unit = parser.ParseCompilationUnit();
        Assert.False(parser.HasErrors, $"Parser errors: {string.Join(", ", parser.Errors)}");

        return unit;
    }

    [Fact]
    public void ParsesEmptyClass()
    {
        var unit = Parse("class Hello { }");

        Assert.Single(unit.TypeDeclarations);
        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        Assert.Equal("Hello", cls.Name);
        Assert.Empty(cls.Members);
    }

    [Fact]
    public void ParsesClassWithInheritance()
    {
        var unit = Parse("class Dog : Animal { }");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        Assert.Equal("Dog", cls.Name);
        Assert.Equal("Animal", cls.BaseClass);
    }

    [Fact]
    public void ParsesClassWithInterface()
    {
        var unit = Parse("class Dog : Animal, IRunnable { }");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        Assert.Equal("Animal", cls.BaseClass);
        Assert.Contains("IRunnable", cls.Interfaces);
    }

    [Fact]
    public void ParsesFieldDeclaration()
    {
        var unit = Parse("class Foo { int x; string name = \"hello\"; }");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        Assert.Equal(2, cls.Members.Count);

        var field1 = Assert.IsType<FieldDeclaration>(cls.Members[0]);
        Assert.Equal("x", field1.Name);
        Assert.Equal("int", field1.Type.Name);

        var field2 = Assert.IsType<FieldDeclaration>(cls.Members[1]);
        Assert.Equal("name", field2.Name);
        Assert.Equal("string", field2.Type.Name);
        Assert.NotNull(field2.Initializer);
    }

    [Fact]
    public void ParsesMethodDeclaration()
    {
        var unit = Parse(@"
            class Calculator {
                int add(int a, int b) {
                    return a + b;
                }
            }
        ");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        var method = Assert.IsType<MethodDeclaration>(cls.Members[0]);

        Assert.Equal("add", method.Name);
        Assert.Equal("int", method.ReturnType.Name);
        Assert.Equal(2, method.Parameters.Count);
        Assert.Equal("a", method.Parameters[0].Name);
        Assert.Equal("int", method.Parameters[0].Type.Name);
        Assert.Equal("b", method.Parameters[1].Name);
        Assert.Equal("int", method.Parameters[1].Type.Name);
    }

    [Fact]
    public void ParsesVoidMethod()
    {
        var unit = Parse(@"
            class Printer {
                void print(string msg) {
                    Console.writeLine(msg);
                }
            }
        ");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        var method = Assert.IsType<MethodDeclaration>(cls.Members[0]);

        Assert.Equal("print", method.Name);
        Assert.Equal("void", method.ReturnType.Name);
    }

    [Fact]
    public void ParsesStaticMethod()
    {
        var unit = Parse(@"
            class App {
                static void main() {
                    Console.writeLine(""hello"");
                }
            }
        ");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        var method = Assert.IsType<MethodDeclaration>(cls.Members[0]);

        Assert.True(method.IsStatic);
        Assert.Equal("void", method.ReturnType.Name);
    }

    [Fact]
    public void ParsesConstructor()
    {
        var unit = Parse(@"
            class Person {
                string name;
                int age;

                Person(string name, int age) {
                    this.name = name;
                    this.age = age;
                }
            }
        ");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        Assert.Equal(3, cls.Members.Count); // 2 fields + 1 constructor

        var ctor = Assert.IsType<ConstructorDeclaration>(cls.Members[2]);
        Assert.Equal(2, ctor.Parameters.Count);
        Assert.Equal("name", ctor.Parameters[0].Name);
        Assert.Equal("string", ctor.Parameters[0].Type.Name);
    }

    [Fact]
    public void ParsesConstructorWithBaseCall()
    {
        var unit = Parse(@"
            class Employee : Person {
                string company;

                Employee(string name, int age, string company) : base(name, age) {
                    this.company = company;
                }
            }
        ");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        var ctor = Assert.IsType<ConstructorDeclaration>(cls.Members[1]);
        Assert.NotNull(ctor.BaseArguments);
        Assert.Equal(2, ctor.BaseArguments!.Count);
    }

    [Fact]
    public void ParsesVirtualAndOverrideMethods()
    {
        var unit = Parse(@"
            class Animal {
                virtual string speak() {
                    return ""..."";
                }
            }
        ");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        var method = Assert.IsType<MethodDeclaration>(cls.Members[0]);

        Assert.True(method.IsVirtual);
        Assert.Equal("speak", method.Name);
    }

    [Fact]
    public void ParsesInterface()
    {
        var unit = Parse(@"
            interface IShape {
                double area();
                string name();
            }
        ");

        var iface = Assert.IsType<InterfaceDeclaration>(unit.TypeDeclarations[0]);
        Assert.Equal("IShape", iface.Name);
        Assert.Equal(2, iface.Methods.Count);
    }

    [Fact]
    public void ParsesEnum()
    {
        var unit = Parse(@"
            enum Color {
                Red = 0,
                Green = 1,
                Blue = 2
            }
        ");

        var enumDecl = Assert.IsType<EnumDeclaration>(unit.TypeDeclarations[0]);
        Assert.Equal("Color", enumDecl.Name);
        Assert.Equal(3, enumDecl.Members.Count);
    }

    [Fact]
    public void ParsesIfElseStatement()
    {
        var unit = Parse(@"
            class Test {
                void check(int x) {
                    if (x > 0) {
                        Console.writeLine(""positive"");
                    } else {
                        Console.writeLine(""non-positive"");
                    }
                }
            }
        ");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        var method = Assert.IsType<MethodDeclaration>(cls.Members[0]);
        var ifStmt = Assert.IsType<IfStatement>(
            ((BlockStatement)method.Body!).Statements[0]);
        Assert.NotNull(ifStmt.ElseBranch);
    }

    [Fact]
    public void ParsesForLoop()
    {
        var unit = Parse(@"
            class Test {
                void loop() {
                    for (int i = 0; i < 10; i++) {
                        Console.writeLine(""hi"");
                    }
                }
            }
        ");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        var method = Assert.IsType<MethodDeclaration>(cls.Members[0]);
        var forStmt = Assert.IsType<ForStatement>(
            ((BlockStatement)method.Body!).Statements[0]);
        Assert.NotNull(forStmt.Initializer);
        Assert.NotNull(forStmt.Condition);
        Assert.NotNull(forStmt.Increment);
    }

    [Fact]
    public void ParsesWhileLoop()
    {
        var unit = Parse(@"
            class Test {
                void loop() {
                    int x = 10;
                    while (x > 0) {
                        x--;
                    }
                }
            }
        ");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        var method = Assert.IsType<MethodDeclaration>(cls.Members[0]);
        Assert.Equal(2, ((BlockStatement)method.Body!).Statements.Count);
    }

    [Fact]
    public void ParsesNewExpression()
    {
        var unit = Parse(@"
            class Test {
                void create() {
                    var p = new Person(""John"", 30);
                }
            }
        ");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        var method = Assert.IsType<MethodDeclaration>(cls.Members[0]);
        var varDecl = Assert.IsType<VariableDeclarationStatement>(
            ((BlockStatement)method.Body!).Statements[0]);
        Assert.IsType<ObjectCreationExpression>(varDecl.Initializer);
    }

    [Fact]
    public void ParsesTypedVariableDeclaration()
    {
        var unit = Parse(@"
            class Test {
                void foo() {
                    int x = 42;
                    string name = ""hello"";
                    double pi = 3.14;
                }
            }
        ");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        var method = Assert.IsType<MethodDeclaration>(cls.Members[0]);
        var stmts = ((BlockStatement)method.Body!).Statements;

        Assert.Equal(3, stmts.Count);
        var decl1 = Assert.IsType<VariableDeclarationStatement>(stmts[0]);
        Assert.Equal("x", decl1.Name);
        Assert.Equal("int", decl1.Type!.Name);
    }

    [Fact]
    public void ParsesVarDeclarationWithInference()
    {
        var unit = Parse(@"
            class Test {
                void foo() {
                    var x = 42;
                    var name = ""hello"";
                }
            }
        ");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        var method = Assert.IsType<MethodDeclaration>(cls.Members[0]);
        var stmts = ((BlockStatement)method.Body!).Statements;

        var decl1 = Assert.IsType<VariableDeclarationStatement>(stmts[0]);
        Assert.Null(decl1.Type); // type inference
    }

    [Fact]
    public void ParsesModuleAndImport()
    {
        var unit = Parse(@"
            module MyApp;
            import System;
            class App { }
        ");

        Assert.NotNull(unit.Module);
        Assert.Equal("MyApp", unit.Module!.Name);
        Assert.Single(unit.Imports);
        Assert.Equal("System", unit.Imports[0].ModuleName);
    }

    [Fact]
    public void ParsesAbstractClass()
    {
        var unit = Parse(@"
            abstract class Shape {
                abstract double area();
            }
        ");

        var cls = Assert.IsType<ClassDeclaration>(unit.TypeDeclarations[0]);
        Assert.True(cls.IsAbstract);
        var method = Assert.IsType<MethodDeclaration>(cls.Members[0]);
        Assert.True(method.IsAbstract);
    }
}
