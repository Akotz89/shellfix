# shellfix

**Make Windows PowerShell stop breaking your commands.**

A three-layer defense system that lets AI coding agents (and humans) run commands transparently from Windows PowerShell terminals. Fixes bash quoting nightmares, path translation failures, `$` expansion, and the infamous red `NativeCommandError` text that makes agents think `git push` failed.

---

## The Problem

AI coding agents (Cursor, Windsurf, GitHub Copilot, Antigravity, etc.) run commands through PowerShell on Windows. Three classes of failures occur constantly:

### Class 1: Bash commands mangled by PowerShell
```bash
grep -c "def " "C:\Users\Me\My Project\app.py"   # path breaks
awk '{print $1, $3}' data.txt                      # $1 eaten
find /project -name "*.py"                         # glob expanded
for i in 1 2 3; do echo "$i"; done                 # PS parse error
```

### Class 2: Complex quoting breaks native commands
```powershell
gh release create v1.0.0 --notes "here's the notes with `backticks`"  # parse error
dotnet build --property:Version="1.0.0-beta.1"                        # mangled
npm run build -- --config '{"minify":true}'                            # stripped
```

### Class 3: Stderr treated as error (red text)
```
git push origin main         # writes progress to stderr → red text
npm install                  # writes warnings to stderr → red text
dotnet build                 # writes diagnostics to stderr → red text
```

Agents see red text, think the command failed, and spiral into desperate workarounds — writing Python subprocess scripts, retrying with different escaping, or giving up entirely.

**shellfix fixes all three classes.**

## What Gets Fixed

| Command | Before | After |
|---|---|---|
| `grep "it's" file` | ❌ **Hangs forever** (unmatched quote) | ✅ Works |
| `awk '{print $1, $3}'` | ❌ `$1`/`$3` expanded to empty | ✅ Works |
| `find "path spaces" -name "*.py"` | ❌ Path split + glob expanded | ✅ Works |
| `for i in 1 2 3; do echo "$i"; done` | ❌ PS parse error | ✅ Works |
| `echo "a" && echo "b"` | ❌ PS 5.1 error | ✅ Works |
| `curl https://example.com` | ❌ Runs `Invoke-WebRequest` | ✅ Runs real curl |
| `C:\Users\Me\My Project\file.py` | ❌ Path not found | ✅ Auto-translated |
| `gh release create --notes "..."` | ❌ Quoting breaks PS parser | ✅ -File fallback |
| `git push origin main` | ❌ Red stderr text | ✅ Clean output |
| `npm install` | ❌ Warnings shown as errors | ✅ Clean output |

## Architecture

```
┌──────────────────────────────────────────────────┐
│  IDE / Agent calls: powershell -Command "..."    │
└─────────────────────┬────────────────────────────┘
                      │
         ┌────────────▼────────────────┐
         │  Layer 1: C# Shim           │
         │  powershell.exe in PATH     │
         │                             │
         │  • Heuristic classifier     │
         │  • Bash → WSL bash -c       │
         │  • Complex PS → temp .ps1   │
         │  • Simple PS → passthrough  │
         │  • Path translation         │
         │  • Quote/glob escaping      │
         │  • WSL crash fallback       │
         └─────┬─────┬─────┬──────────┘
               │     │     │
      ┌────────▼┐ ┌──▼──┐ ┌▼─────────────────────┐
      │WSL bash │ │-File│ │Real powershell.exe    │
      └─────────┘ └─────┘ │+ Profile (Layer 2+3) │
                           │                      │
                           │• 50+ bash wrappers   │
                           │• Pipeline support    │
                           │• Alias deconfliction │
                           │• NativeCommandError  │
                           │  suppression (git,   │
                           │  npm, gh, dotnet...) │
                           │• UTF-8 enforcement   │
                           └──────────────────────┘
```

### Layer 1: Compiled C# Shim

A .NET 8 executable named `powershell.exe` placed earlier in PATH. When the IDE calls `powershell -Command "..."`, the shim:

1. **Classifies** the command as bash or PowerShell
2. For **bash**: escapes quotes, translates paths, re-quotes globs, routes to `wsl.exe -- bash -c` via `.NET ArgumentList`
3. For **complex PS**: writes to a temp `.ps1` file and runs with `-File` (bypasses argument parser entirely)
4. For **simple PS**: passes through to real `powershell.exe`
5. **Falls back** to real PowerShell if WSL crashes

### Layer 2: PowerShell Profile — Bash Wrappers

Creates function wrappers for 50+ bash commands that handle path translation, quoting, dollar-sign escaping, and pipeline support.

### Layer 3: PowerShell Profile — Native Tool Wrappers

Wraps `git`, `npm`, `npx`, `dotnet`, `gh`, `cargo`, `rustc`, `docker`, and `kubectl` in functions that merge stderr to stdout as plain strings. This prevents PS 5.1 from treating normal stderr output as errors (red text), while preserving real exit codes via `$LASTEXITCODE`.

## Requirements

- Windows 10/11 with WSL2
- A WSL distribution (default: Ubuntu-24.04, configurable)
- .NET 8 SDK (for building from source) — or use a [pre-built release](https://github.com/Akotz89/shellfix/releases)
- PowerShell 5.1+ (comes with Windows)

## Installation

### Quick Install

```powershell
git clone https://github.com/Akotz89/shellfix.git
cd shellfix
.\install.ps1
```

### Pre-built Binary

Download from [Releases](https://github.com/Akotz89/shellfix/releases), then:

```powershell
.\install.ps1 -SkipBuild
```

### Verify

```powershell
.\test.ps1
```

## Configuration

### WSL Distribution

```powershell
.\install.ps1 -WslDistro "Ubuntu-22.04"
```

### Controls

| Control | How |
|---|---|
| **Disable shim** | `$env:PWSH_SHIM_BYPASS = "1"` |
| **Debug mode** | `$env:PWSH_SHIM_DEBUG = "1"` |
| **Uninstall** | `.\install.ps1 -Uninstall` |

## How It Works

### The Four-Layer Quoting Problem

When an IDE runs `powershell -Command "..."`, the string passes through four interpretation layers:

1. **IDE process spawner** → strips outer quotes
2. **Windows CreateProcess** → strips/mangles inner quotes  
3. **PowerShell parser** → interprets `$`, backticks, `&&`, single quotes
4. **Target shell (bash/cmd)** → interprets remaining special chars

Each layer has different escaping rules. A single `'` in "it's" becomes an unmatched quote in bash (hang). A `$1` in awk becomes empty (PS expansion). Progress text on stderr becomes red error text (PS 5.1 bug).

### How shellfix Solves Each Layer

| Layer | Problem | Fix |
|---|---|---|
| 2 | CreateProcess mangles quotes | `.NET ArgumentList` bypasses string quoting |
| 3 | PS expands `$`, chokes on `&&` | Shim intercepts before PS sees it |
| 3 | PS chokes on complex quoting | `-File` fallback writes to temp `.ps1` |
| 3 | PS treats stderr as error | Profile wraps native tools with `2>&1` conversion |
| 4 | Bash gets unescaped `'` and `$` | Shim escapes `'` → `\'` and `$` → `\$` |

### Path Translation

```
C:\Users\Me\My Project\file.py
  → '/mnt/c/Users/Me/My Project/file.py'

C:\Users\Me\code\app.py
  → /mnt/c/Users/Me/code/app.py
```

## Testing

```powershell
.\test.ps1           # Standard run
.\test.ps1 -Verbose  # Show output details
```

Covers all three failure classes with 25+ tests.

## FAQ

**Q: Does this work with Cursor / Windsurf / Copilot / Antigravity?**  
A: Yes. Any tool that calls `powershell -Command "..."` benefits.

**Q: Will this break my normal PowerShell?**  
A: No. Kill switch: `$env:PWSH_SHIM_BYPASS = "1"`.

**Q: Why do I still see red text sometimes?**  
A: Only tools in the wrapper list (`git`, `npm`, `gh`, etc.) are protected. If you find another tool that triggers NativeCommandError, add it to the `$nativeTools` array in the profile.

**Q: What about PowerShell 7?**  
A: PS 7 fixes the NativeCommandError issue natively. The shim and bash wrappers still provide value for path translation and bash routing.

**Q: Why not just switch to bash/Git Bash?**  
A: Most IDE agent frameworks hardcode `powershell -Command` on Windows. There's no setting to change this in Cursor, Windsurf, or Antigravity as of 2026.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
