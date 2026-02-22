# ggLang — Coding Standards & Best Practices

This document defines the official coding standards, naming conventions, project structure,
and best practices for writing ggLang code. Follow these guidelines for consistency and
maintainability across all ggLang projects.

---

## Table of Contents

1. [Project Structure](#1-project-structure)
2. [Naming Conventions](#2-naming-conventions)
3. [Formatting](#3-formatting)
4. [Classes](#4-classes)
5. [Methods](#5-methods)
6. [Variables & Fields](#6-variables--fields)
7. [Control Flow](#7-control-flow)
8. [Annotations](#8-annotations)
9. [Libraries](#9-libraries)
10. [Comments & Documentation](#10-comments--documentation)
11. [Error Handling Patterns](#11-error-handling-patterns)
12. [Anti-Patterns](#12-anti-patterns)

---

## 1. Project Structure

A ggLang project follows this standard layout:

```
my_project/
├── src/
│   ├── main.gg              # Entry point (Program class)
│   ├── models/              # Domain models (pure data classes)
│   ├── services/            # Business logic
│   └── utils/               # Utility classes
├── libs/                    # Local .lib.gg libraries
├── tests/                   # Test files (.test.gg)
├── .gitignore
└── README.md
```

**Rules:**
- One class per file. File name must match the class name.
- Entry point lives in `src/main.gg` inside a class named `Program`.
- Group related classes in subdirectories by responsibility.
- Local libraries go in `libs/` with the `.lib.gg` extension.

**Examples:**

| Class | File |
|---|---|
| `UserRepository` | `src/models/UserRepository.gg` |
| `AuthService` | `src/services/AuthService.gg` |
| `StringUtils` | `libs/StringUtils.lib.gg` |

---

## 2. Naming Conventions

### Classes

Use **PascalCase**. Names should be nouns that clearly describe the class's responsibility.

```csharp
// ✅ Good
class UserAccount { }
class HttpRequest { }
class InvoiceService { }

// ❌ Bad
class userAccount { }
class http_request { }
class Svc { }
```

### Methods

Use **camelCase**. Names should be verbs or verb phrases.

```csharp
// ✅ Good
void sendEmail(string to) { }
int calculateTotal(int price, int qty) { }
bool isAvailable() { }
string formatDate(int day, int month, int year) { }

// ❌ Bad
void SendEmail(string to) { }
void send_email(string to) { }
int calc(int p, int q) { }
```

### Fields

Use **camelCase**. Avoid prefixes like `m_`, `_`, or `s_`.

```csharp
// ✅ Good
string name;
int totalAmount;
bool isActive;

// ❌ Bad
string _name;
int m_totalAmount;
bool bIsActive;
```

### Parameters & Local Variables

Use **camelCase**. Be descriptive — avoid single letters except in short loops.

```csharp
// ✅ Good
int calculateDiscount(int basePrice, int discountRate) {
    int discountedValue = basePrice - (basePrice * discountRate);
    return discountedValue;
}

// ❌ Bad
int cd(int p, int r) {
    int x = p - (p * r);
    return x;
}
```

### Constants

Use **SCREAMING_SNAKE_CASE** with `const` or `readonly`.

```csharp
// ✅ Good
const int MAX_RETRIES = 3;
const string DEFAULT_HOST = "localhost";
readonly int port = 8080;

// ❌ Bad
const int maxRetries = 3;
const string defaulthost = "localhost";
```

### Library Classes

Use **PascalCase** matching the file name (without `.lib.gg`).

```csharp
// File: libs/MathUtils.lib.gg
[@Library("MathUtils", "1.0.0")]
class MathUtils { }
```

---

## 3. Formatting

### Indentation

Use **4 spaces**. Never use tabs.

```csharp
// ✅ Good
class Order {
    int id;

    void process() {
        if (this.id > 0) {
            Console.writeLine("processing");
        }
    }
}
```

### Braces

Always use braces, even for single-line bodies. Opening brace on the same line.

```csharp
// ✅ Good
if (x > 0) {
    return x;
}

// ❌ Bad — no braces
if (x > 0)
    return x;

// ❌ Bad — brace on next line
if (x > 0)
{
    return x;
}
```

### Blank Lines

- One blank line between methods.
- Two blank lines between top-level classes (if in the same file).
- No trailing blank lines inside method bodies.

```csharp
class Calculator {
    int result;

    Calculator() {
        this.result = 0;
    }

    int add(int a, int b) {
        return a + b;
    }

    int subtract(int a, int b) {
        return a - b;
    }
}
```

### Line Length

Keep lines under **100 characters**. Break long method calls and condition chains.

```csharp
// ✅ Good
bool isEligible = user.isActive()
    && user.getAge() >= 18
    && user.hasVerifiedEmail();

// ❌ Bad
bool isEligible = user.isActive() && user.getAge() >= 18 && user.hasVerifiedEmail();
```

### Spacing

- Space after keywords: `if (`, `for (`, `while (`
- No space before parentheses in method calls: `calc.add(a, b)`
- Space around binary operators: `a + b`, `x > 0`, `n * 2`
- No space inside parentheses: `(a + b)`, not `( a + b )`

```csharp
// ✅ Good
for (int i = 0; i < 10; i++) {
    int result = i * 2 + 1;
    if (result > 5) {
        Console.writeLine(result);
    }
}

// ❌ Bad
for(int i=0;i<10;i++){
    int result=i*2+1;
    if(result>5){Console.writeLine(result);}
}
```

---

## 4. Classes

### Single Responsibility

Each class should do one thing and do it well. If a class name contains "And", it probably
does too much.

```csharp
// ✅ Good — focused
class EmailSender {
    void send(string to, string subject, string body) { }
}

class EmailValidator {
    bool isValid(string email) { }
}

// ❌ Bad — too broad
class EmailSenderAndValidatorAndLogger {
    void send(string to) { }
    bool isValid(string email) { }
    void log(string message) { }
}
```

### Constructor Guidelines

- Constructors initialize all fields.
- Avoid complex logic in constructors.
- Use `this.fieldName` to distinguish fields from parameters.

```csharp
// ✅ Good
class User {
    string name;
    string email;
    bool active;

    User(string name, string email) {
        this.name = name;
        this.email = email;
        this.active = true;
    }
}

// ❌ Bad — no field initialization
class User {
    string name;

    User() { }
}
```

### Access Modifiers

Declare the most restrictive modifier justified. Prefer `private` for fields.

```csharp
class BankAccount {
    private int balance;     // internal state — private
    private string owner;    // internal state — private

    BankAccount(string owner, int initial) {
        this.owner = owner;
        this.balance = initial;
    }

    public int getBalance() {         // external interface — public
        return this.balance;
    }

    public void deposit(int amount) { // external interface — public
        if (amount > 0) {
            this.balance = this.balance + amount;
        }
    }

    private bool hasEnough(int amount) {  // helper — private
        return this.balance >= amount;
    }
}
```

### Inheritance

- Prefer composition over inheritance when behavior doesn't naturally extend.
- Only override methods that need different behavior.
- Always call `base()` in constructors of derived classes.

```csharp
// ✅ Good — natural inheritance hierarchy
class Shape {
    string color;

    Shape(string color) {
        this.color = color;
    }

    virtual double area() {
        return 0.0;
    }
}

class Circle : Shape {
    double radius;

    Circle(string color, double radius) : base(color) {
        this.radius = radius;
    }

    override double area() {
        return 3.14159 * this.radius * this.radius;
    }
}
```

### Static Classes / Utility Classes

Use static methods for stateless utilities. Do not instantiate utility classes.

```csharp
// ✅ Good
class MathHelper {
    static int clamp(int value, int min, int max) {
        if (value < min) { return min; }
        if (value > max) { return max; }
        return value;
    }

    static bool isPrime(int n) {
        if (n < 2) { return false; }
        int i = 2;
        while (i * i <= n) {
            if (n % i == 0) { return false; }
            i = i + 1;
        }
        return true;
    }
}
```

---

## 5. Methods

### Small and Focused

A method should do one thing. If it is longer than ~20 lines, consider splitting it.

```csharp
// ✅ Good — each method is focused
class OrderProcessor {
    bool validateOrder(int orderId, int qty) {
        return orderId > 0 && qty > 0;
    }

    int calculateTotal(int unitPrice, int qty) {
        return unitPrice * qty;
    }

    void processOrder(int orderId, int qty, int unitPrice) {
        if (!this.validateOrder(orderId, qty)) {
            Console.writeLine("Invalid order");
            return;
        }
        int total = this.calculateTotal(unitPrice, qty);
        Console.writeLine(total);
    }
}
```

### Return Early

Use early returns to reduce nesting and improve readability.

```csharp
// ✅ Good — guard clauses first
int divide(int a, int b) {
    if (b == 0) {
        Console.writeLine("Cannot divide by zero");
        return 0;
    }
    return a / b;
}

// ❌ Bad — deep nesting
int divide(int a, int b) {
    if (b != 0) {
        return a / b;
    } else {
        Console.writeLine("Cannot divide by zero");
        return 0;
    }
}
```

### Boolean Methods

Name boolean methods as questions using `is`, `has`, `can`, `should`.

```csharp
// ✅ Good
bool isActive() { return this.active; }
bool hasPermission(string role) { return this.role == role; }
bool canProceed() { return this.status == "ready"; }

// ❌ Bad
bool active() { return this.active; }
bool checkPermission(string role) { return this.role == role; }
```

### Method Parameters

- Limit to 3–4 parameters. More than that suggests a missing abstraction.
- Never pass flags (booleans) to control method behavior — split into two methods.

```csharp
// ✅ Good
void sendWelcomeEmail(string to) { }
void sendPasswordResetEmail(string to) { }

// ❌ Bad — flag parameter
void sendEmail(string to, bool isWelcome) { }
```

---

## 6. Variables & Fields

### Declare Close to Use

Declare local variables as close to their first use as possible.

```csharp
// ✅ Good
void process() {
    int count = 0;
    for (int i = 0; i < 10; i++) {
        count = count + i;
    }
    string result = "Total: " + count;
    Console.writeLine(result);
}

// ❌ Bad — all variables at top
void process() {
    int count;
    int i;
    string result;
    count = 0;
    for (i = 0; i < 10; i++) {
        count = count + i;
    }
    result = "Total: " + count;
    Console.writeLine(result);
}
```

### Use `var` Judiciously

Use `var` when the type is obvious from the right-hand side. Prefer explicit types otherwise.

```csharp
// ✅ Good — type is obvious
var count = 0;
var name = "Alice";
var user = new User("Alice", 30);

// ❌ Bad — type not obvious
var result = calculate();    // what type is this?
var x = getRecord(id);       // unknown type

// ✅ Better — explicit when not obvious
int result = calculate();
User record = getRecord(id);
```

### Avoid Magic Numbers

Replace raw numeric values with named constants.

```csharp
// ✅ Good
const int MAX_LOGIN_ATTEMPTS = 5;
const int SESSION_TIMEOUT_MINUTES = 30;

if (attempts >= MAX_LOGIN_ATTEMPTS) {
    Console.writeLine("Account locked");
}

// ❌ Bad
if (attempts >= 5) {
    Console.writeLine("Account locked");
}
```

---

## 7. Control Flow

### Prefer Positive Conditions

Write conditions in the positive form when possible — they are easier to read.

```csharp
// ✅ Good
if (user.isActive()) {
    allowAccess();
}

// ❌ Harder to reason about
if (!user.isInactive()) {
    allowAccess();
}
```

### Avoid Deep Nesting

Refactor deeply nested code using early returns or extracted methods.

```csharp
// ✅ Good — flat with early returns
bool processPayment(int amount, bool isVerified) {
    if (amount <= 0) {
        Console.writeLine("Invalid amount");
        return false;
    }
    if (!isVerified) {
        Console.writeLine("Not verified");
        return false;
    }
    Console.writeLine("Payment processed");
    return true;
}

// ❌ Bad — deeply nested
bool processPayment(int amount, bool isVerified) {
    if (amount > 0) {
        if (isVerified) {
            Console.writeLine("Payment processed");
            return true;
        } else {
            Console.writeLine("Not verified");
            return false;
        }
    } else {
        Console.writeLine("Invalid amount");
        return false;
    }
}
```

### Loop Guidelines

- Use `for` when the number of iterations is known.
- Use `while` for condition-based iteration.
- Avoid modifying the loop variable inside a `for` body.
- Keep loop bodies short — extract to a method if complex.

```csharp
// ✅ Good
void printNumbers(int limit) {
    for (int i = 1; i <= limit; i++) {
        Console.writeLine(i);
    }
}

// ✅ Good — while for condition
void readUntilEmpty() {
    string line = Console.readLine();
    while (line != "") {
        Console.writeLine(line);
        line = Console.readLine();
    }
}
```

---

## 8. Annotations

### Syntax

Annotations use the `[@Name]` or `[@Name("arg")]` syntax and appear immediately before
the declaration they annotate — no blank lines between the annotation and its target.

```csharp
// ✅ Good
[@Library("Math", "1.0.0")]
class Math {
    static int abs(int n) { }
}

// ❌ Bad — blank line between annotation and class
[@Library("Math", "1.0.0")]

class Math { }
```

### Library Annotation

Every `.lib.gg` file must declare its main class with `[@Library("Name", "Version")]`.
The name must match the file name (without `.lib.gg`).

```csharp
// File: libs/Formatter.lib.gg
[@Library("Formatter", "1.0.0")]
class Formatter {
    static string pad(string text, int width) { }
}
```

### Version Format

Library versions follow **semantic versioning**: `MAJOR.MINOR.PATCH`.

| Change | Bump |
|---|---|
| Breaking API change | `MAJOR` |
| New features, backward compatible | `MINOR` |
| Bug fixes | `PATCH` |

---

## 9. Libraries

### Structure

A standard library file contains one primary class and optionally supporting classes.
The primary class carries the `[@Library]` annotation.

```csharp
// libs/Http.lib.gg

[@Library("Http", "1.0.0")]
class Http {
    static string get(string url) { }
    static string post(string url, string body) { }
}

// Supporting classes — no annotation needed
class HttpRequest {
    string method;
    string url;

    HttpRequest() {
        this.method = "GET";
        this.url = "";
    }
}
```

### Library Design Principles

- Libraries should have **no side effects** at the class level.
- All mutable state should be inside instances, not in static fields.
- Prefer **static utility methods** for stateless operations.
- Keep library classes focused — do not bundle unrelated utilities in one library.

```csharp
// ✅ Good — focused library
[@Library("Validator", "1.0.0")]
class Validator {
    static bool isEmail(string value) { }
    static bool isUrl(string value) { }
    static bool isNotEmpty(string value) { }
}

// ❌ Bad — mixed concerns
[@Library("Utils", "1.0.0")]
class Utils {
    static bool isEmail(string value) { }
    static string formatDate(int day) { }
    static int add(int a, int b) { }
    static void printHeader() { }
}
```

---

## 10. Comments & Documentation

### When to Comment

Comment **why**, not **what**. The code explains what — comments explain intent.

```csharp
// ✅ Good — explains why
// Use base 1 to match the external API contract that indexes from 1
int index = position + 1;

// ❌ Bad — restates the code
// Add 1 to position
int index = position + 1;
```

### Method Documentation

Use `///` for public-facing documentation comments.

```csharp
/// Calculates compound interest over a number of periods.
/// formula: principal * (1 + rate)^periods - principal
int compoundInterest(int principal, int rate, int periods) {
    int result = principal;
    for (int i = 0; i < periods; i++) {
        result = result + (result * rate);
    }
    return result - principal;
}
```

### Class Documentation

Add a brief description at the top of each class explaining its purpose.

```csharp
/// Manages user authentication state and session tokens.
class AuthManager {
    string currentToken;
    bool authenticated;

    AuthManager() {
        this.currentToken = "";
        this.authenticated = false;
    }
}
```

### Avoid Commented-Out Code

Do not leave commented-out code in the codebase. Use version control to recover old code.

```csharp
// ❌ Bad — commented-out dead code
void process() {
    // int old = legacyCalc(x);
    int result = newCalc(x);
    // Console.writeLine("debug: " + old);
    Console.writeLine(result);
}

// ✅ Good — clean
void process() {
    int result = newCalc(x);
    Console.writeLine(result);
}
```

---

## 11. Error Handling Patterns

ggLang does not have exceptions yet. Use return values and guard clauses to handle errors.

### Guard Clauses

Validate inputs at the top of methods and return early on failure.

```csharp
int divide(int a, int b) {
    if (b == 0) {
        Console.writeLine("error: division by zero");
        return 0;
    }
    return a / b;
}
```

### Result-Style Pattern

For operations that can fail, return a sentinel value and provide a separate status method.

```csharp
class Parser {
    bool lastError;
    string lastErrorMessage;

    Parser() {
        this.lastError = false;
        this.lastErrorMessage = "";
    }

    int parseInt(string value) {
        if (value == "") {
            this.lastError = true;
            this.lastErrorMessage = "Empty input";
            return 0;
        }
        this.lastError = false;
        return 0; // actual parsing logic here
    }

    bool hasError() { return this.lastError; }
    string getError() { return this.lastErrorMessage; }
}

class Program {
    static void main() {
        var parser = new Parser();
        int result = parser.parseInt("");
        if (parser.hasError()) {
            Console.writeLine("Parse error: " + parser.getError());
        }
    }
}
```

---

## 12. Anti-Patterns

Avoid the following patterns in ggLang code.

### God Class

A class that knows too much or does too much.

```csharp
// ❌ Bad
class Application {
    void connectDatabase() { }
    void renderUI() { }
    void sendEmail() { }
    void parseConfig() { }
    void logError() { }
    void calculateTax() { }
}

// ✅ Good — split by responsibility
class DatabaseConnector { void connect() { } }
class EmailSender { void send(string to) { } }
class TaxCalculator { int calculate(int amount) { } }
```

### Long Parameter Lists

More than 4 parameters signals a missing abstraction.

```csharp
// ❌ Bad
void createUser(string firstName, string lastName, string email, int age, string role, bool active) { }

// ✅ Good — introduce a data class
class UserRequest {
    string firstName;
    string lastName;
    string email;
    int age;
    string role;
    bool active;

    UserRequest(string firstName, string lastName, string email, int age) {
        this.firstName = firstName;
        this.lastName = lastName;
        this.email = email;
        this.age = age;
        this.role = "user";
        this.active = true;
    }
}

void createUser(UserRequest request) { }
```

### Primitive Obsession

Using primitive types where a dedicated class would be clearer.

```csharp
// ❌ Bad — coordinates as raw integers
void moveTo(int x, int y) { }

// ✅ Good — dedicated class
class Point {
    int x;
    int y;

    Point(int x, int y) {
        this.x = x;
        this.y = y;
    }
}

void moveTo(Point target) { }
```

### Empty Catch / Silent Failures

Never silently swallow errors.

```csharp
// ❌ Bad — failure goes unnoticed
int load(string key) {
    if (key == "") {
        return 0; // silently returns 0 — caller has no idea it failed
    }
    return 0;
}

// ✅ Good — communicate failure
int load(string key) {
    if (key == "") {
        Console.writeLine("error: key cannot be empty");
        return -1; // documented sentinel value
    }
    return 0;
}
```

---

## Quick Reference

| Rule | ✅ Do | ❌ Don't |
|---|---|---|
| Class names | `PascalCase` | `camelCase`, `snake_case` |
| Method names | `camelCase` | `PascalCase`, `snake_case` |
| Field names | `camelCase` | `_prefixed`, `m_prefixed` |
| Constants | `SCREAMING_SNAKE_CASE` | `camelCase` |
| Braces | Same line | Next line |
| Indentation | 4 spaces | Tabs |
| One class per file | `Person.gg` for `Person` | Multiple classes per file |
| Guard clauses | Return early | Deep nesting |
| `var` | When type is obvious | When type is unclear |
| Magic numbers | Named constants | Raw literals |
| Comments | Explain **why** | Restate the code |
| Commented-out code | Delete it | Leave it |
| Method length | ≤ 20 lines | Unbounded |
| Parameters | ≤ 4 | > 4 (extract a class) |

---

*ggLang v0.3.0-beta — Subject to change as the language evolves.*
