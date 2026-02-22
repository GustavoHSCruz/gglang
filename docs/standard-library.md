# ggLang Standard Library Overview

ggLang ships with built-in `.lib.gg` libraries in the `libs/` directory. These files are intended to be imported and used from application code.

## Using a Library

```csharp
import Math;

class Program {
    static void main() {
        Console.writeLine(Math.abs(-42));
    }
}
```

## Available Libraries

| Library | File | Highlights |
| --- | --- | --- |
| `Base` | `libs/Base.lib.gg` | Type checks, string helpers, numeric helpers, logging helpers, timestamps |
| `Collections` | `libs/Collections.lib.gg` | `HashMap`, `HashSet`, `LinkedList`, `Stack`, `Queue` |
| `Crypto` | `libs/Crypto.lib.gg` | `sha256`, `md5`, `sha1`, `hmacSha256`, Base64/Hex, random/UUID |
| `Files` | `libs/Files.lib.gg` | File I/O, directory ops, path helpers |
| `Http` | `libs/Http.lib.gg` | `get/post/put/delete`, request/response helpers |
| `Math` | `libs/Math.lib.gg` | `abs`, `max`, `min`, `clamp` |
| `Network` | `libs/Network.lib.gg` | DNS/host utilities, TCP/UDP helpers, URL utilities |
| `OS` | `libs/OS.lib.gg` | Environment, process execution, clock/time, platform info |
| `StringUtils` | `libs/StringUtils.lib.gg` | Basic string utilities (`repeat`, `isEmpty`) |

## Installation in Projects

You can copy standard libraries into local `libs/` using `gg pkg`:

```bash
gg pkg install Math
gg pkg install Collections
gg pkg list
```

## Notes

- Standard libraries use `[@Library("Name", "Version")]` annotations.
- `.lib.gg` files are not intended as direct entry-point compilation targets.
- Libraries installed via `make install`, `build.sh install`, or `gg pkg install` are marked read-only.
- On Linux/macOS, the installer/CLI also attempts immutable flags (`chattr +i` / `chflags uchg`) for stronger protection.
- To edit a locked local library manually, unlock first:
  - Linux: `chattr -i libs/YourLib.lib.gg && chmod u+w libs/YourLib.lib.gg`
  - macOS: `chflags nouchg libs/YourLib.lib.gg && chmod u+w libs/YourLib.lib.gg`
