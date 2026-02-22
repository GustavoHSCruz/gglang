# ggLang Language Specification

## Overview

ggLang is a statically-typed, object-oriented programming language with C#-style syntax.
It compiles to native binaries through C transpilation (GCC backend).

## Syntax

### Type System

ggLang uses C#-style type declarations where the type comes before the name:

```csharp
int x = 42;
string name = "hello";
double pi = 3.14;
bool active = true;
char letter = 'A';
```

Type inference is available with `var`:

```csharp
var x = 42;       // inferred as int
var name = "hi";   // inferred as string
```

### Primitive Types

| Type     | Description           | C Mapping        |
|----------|-----------------------|------------------|
| `int`    | 32-bit integer        | `int`            |
| `long`   | 64-bit integer        | `long long`      |
| `float`  | 32-bit floating point | `float`          |
| `double` | 64-bit floating point | `double`         |
| `bool`   | Boolean               | `bool`           |
| `char`   | Character             | `char`           |
| `string` | String                | `const char*`    |
| `byte`   | Unsigned byte         | `unsigned char`  |
| `short`  | 16-bit integer        | `short`          |
| `void`   | No value              | `void`           |

### Classes

All code must be inside classes. ggLang is 100% object-oriented.

```csharp
class MyClass {
    // fields
    int value;
    string name;

    // constructor (uses class name)
    MyClass(int value, string name) {
        this.value = value;
        this.name = name;
    }

    // methods (return type before name)
    int getValue() {
        return this.value;
    }

    void setValue(int v) {
        this.value = v;
    }

    // static methods
    static void main() {
        var obj = new MyClass(42, "test");
        Console.writeLine(obj.getName());
    }
}
```

### Inheritance

Single class inheritance with `:` syntax:

```csharp
class Animal {
    string name;

    Animal(string name) {
        this.name = name;
    }

    virtual string speak() {
        return "...";
    }
}

class Dog : Animal {
    Dog(string name) : base(name) { }

    override string speak() {
        return "Woof!";
    }
}
```

### Interfaces

```csharp
interface IShape {
    double area();
    string describe();
}

class Circle : IShape {
    double radius;

    Circle(double radius) {
        this.radius = radius;
    }

    double area() {
        return 3.14159 * this.radius * this.radius;
    }

    string describe() {
        return "Circle";
    }
}
```

### Enums

```csharp
enum Color {
    Red = 0,
    Green = 1,
    Blue = 2
}
```

### Access Modifiers

- `public` (default) — accessible from everywhere
- `private` — accessible only within the class
- `protected` — accessible within the class and derived classes

### Method Modifiers

- `static` — class-level method (no `this`)
- `virtual` — can be overridden in derived classes
- `override` — overrides a virtual method
- `abstract` — must be implemented by derived classes
- `sealed` — prevents further overriding

### Control Flow

```csharp
// If-else
if (condition) {
    // ...
} else if (other) {
    // ...
} else {
    // ...
}

// For loop
for (int i = 0; i < 10; i++) {
    // ...
}

// While loop
while (condition) {
    // ...
}

// ForEach
foreach (int item in collection) {
    // ...
}
```

### Operators

| Category    | Operators                           |
|-------------|-------------------------------------|
| Arithmetic  | `+`, `-`, `*`, `/`, `%`            |
| Comparison  | `==`, `!=`, `<`, `>`, `<=`, `>=`   |
| Logical     | `&&`, `\|\|`, `!`                   |
| Bitwise     | `&`, `\|`, `^`, `~`, `<<`, `>>`    |
| Assignment  | `=`, `+=`, `-=`, `*=`, `/=`        |
| Increment   | `++`, `--`                          |
| Other       | `new`, `as`, `.`                    |

### Built-in Classes

#### Console

```csharp
Console.writeLine("text");    // print with newline
Console.write("text");        // print without newline
Console.readLine();            // read line from stdin
```

#### Math

```csharp
Math.abs(x);
Math.sqrt(x);
Math.pow(x, y);
Math.min(a, b);
Math.max(a, b);
```

## Compilation Pipeline

```
.gg source → Lexer → Parser (AST) → Semantic Analyzer → C Code Generator → GCC → Native Binary
```

1. **Lexer**: Tokenizes source code
2. **Parser**: Builds AST using recursive descent
3. **Semantic Analyzer**: 3-pass analysis (types → members → bodies)
4. **C Code Generator**: Emits C code with vtables for OOP
5. **GCC**: Compiles C to native binary
