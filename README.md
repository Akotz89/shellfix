# shellfix

**Make Windows PowerShell stop breaking your bash commands.**

A two-layer defense system that lets AI coding agents (and humans) run bash commands transparently from Windows PowerShell terminals. No more quoting nightmares, no more path translation failures, no more `$` expansion eating your awk patterns.

---

## The Problem

AI coding agents (Cursor, Windsurf, GitHub Copilot, Antigravity, etc.) run commands through PowerShell on Windows. When they emit bash commands like:

```bash
grep -c "def " "C:\Users\Me\My Project\app.py"
awk '{print $1, $3}' data.txt
find /project -name "*.py" | xargs grep "TODO"
for i in 1 2 3; do echo "num $i"; done
```

**Every single one breaks.** PowerShell mangles paths, eats `$` signs, chokes on `&&`, strips quotes, and turns `curl` into `Invoke-WebRequest`.

This project fixes all of it.

## What Gets Fixed

| Command | Before | After |
|---|---|---|
| `grep "it's" file` | ❌ **Hangs forever** (unmatched quote) | ✅ Works |
| `awk '{print $1, $3}'` | ❌ `$1`/`$3` expanded to empty | ✅ Works |
| `jq -n '{"a":1}'` | ❌ Quotes stripped by Windows | ✅ Works |
| `find "path spaces" -name "*.py"` | ❌ Path split + glob expanded | ✅ Works |
| `for i in 1 2 3; do echo "$i"; done` | ❌ PS parse error | ✅ Works |
| `echo "a" && echo "b"` | ❌ PS 5.1 error | ✅ Works |
| `if [ -f /etc/os-release ]; then...fi` | ❌ PS parse error | ✅ Works |
| `curl https://example.com` | ❌ Runs `Invoke-WebRequest` | ✅ Runs real curl |
| `C:\Users\Me\My Project\file.py` | ❌ Path not found | ✅ Auto-translated to `/mnt/c/...` |

## Architecture

```
┌──────────────────────────────────────────────────┐
│  IDE / Agent calls: powershell -Command "..."    │
└─────────────────────┬────────────────────────────┘
                      │
         ┌────────────▼────────────────┐
         │  Layer 1: C# Shim          │
         │  powershell.exe in PATH     │
         │                             │
         │  • Heuristic classifier     │
         │  • Bash → WSL bash -c       │
         │  • PS → real powershell.exe │
         │  • Path translation         │
         │  • Apostrophe escaping      │
         │  • $ sign preservation      │
         │  • Glob re-quoting          │
         │  • WSL crash fallback       │
         └─────┬──────────┬────────────┘
               │          │
      ┌────────▼──┐   ┌───▼──────────────────┐
      │ WSL bash  │   │ Real powershell.exe   │
      └───────────┘   │ + Profile (Layer 2)   │
                      │                       │
                      │ • 50+ bash wrappers   │
                      │ • Pipeline support    │
                      │ • Alias deconfliction │
                      │ • UTF-8 enforcement   │
                      │ • WSL health guard    │
                      └───────────────────────┘
```

### Layer 1: Compiled C# Shim

A .NET 8 executable named `powershell.exe` placed earlier in PATH than the real one. When the IDE calls `powershell -Command "grep ..."`, the shim:

1. **Classifies** the command as bash or PowerShell using heuristic analysis
2. **Escapes** single quotes (`'` → `\'`), dollar signs (`$` → `\$`), and re-quotes glob patterns
3. **Translates** Windows paths to WSL paths (handles spaces, dots, hyphens)
4. **Routes** bash to `wsl.exe -d <distro> -- bash -c` via `.NET ArgumentList` (no shell quoting layer)
5. **Falls back** to real PowerShell if WSL crashes or is unavailable

### Layer 2: PowerShell Profile

Loaded when commands go through real PowerShell. Creates function wrappers for 50+ bash commands that:

1. **Convert** each argument's path from Windows to WSL format
2. **Quote** arguments safely for `bash -c`
3. **Escape** `$` and `"` for the PowerShell→WSL transit
4. **Support pipelines** (`grep ... | sort | uniq | wc -l`)
5. **Remove** conflicting PS aliases (`curl`, `diff`, `sort`, `cat`, etc.)

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
# Extract and run
.\install.ps1 -SkipBuild
```

### Verify

```powershell
.\test.ps1
```

## Configuration

### WSL Distribution

Pass your distro name during install:

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

### The Quoting Problem

When an IDE runs `powershell -Command 'grep "it's" file'`, the string passes through **four layers** of interpretation:

1. **IDE process spawner** → strips outer quotes
2. **Windows CreateProcess** → strips/mangles inner quotes  
3. **PowerShell parser** → interprets `$`, backticks, `&&`
4. **WSL/bash** → interprets remaining `'`, `"`, `$`

Each layer has different escaping rules. A single `'` in "it's" becomes an unmatched quote in bash, causing an infinite hang. A `$1` in awk becomes empty because PS expands it.

### Our Solution

**Layer 1 (Shim)** intercepts at step 2, before PowerShell ever sees the string. It classifies, escapes, and routes directly to WSL using `.NET ArgumentList` which bypasses `CreateProcess` string quoting entirely.

**Layer 2 (Profile)** handles commands that reach PowerShell (step 3). Each command is wrapped in a function that builds a properly-escaped `bash -c` string with path translation.

### Path Translation

Windows paths are automatically converted:

```
C:\Users\Me\My Project\file.py
  → '/mnt/c/Users/Me/My Project/file.py'

C:\Users\Me\code\app.py
  → /mnt/c/Users/Me/code/app.py

\\wsl.localhost\Ubuntu-24.04\home\me\file
  → /home/me/file
```

## Testing

```powershell
.\test.ps1         # Standard run
.\test.ps1 -Verbose # Show output details
```

Runs 23+ tests covering bash routing, quoting, pipelines, exit codes, and health checks.

## FAQ

**Q: Does this work with Cursor / Windsurf / Copilot / Antigravity?**  
A: Yes. Any tool that calls `powershell -Command "..."` benefits from both layers.

**Q: Will this break my normal PowerShell?**  
A: No. The shim only activates on `-Command` invocations. Kill switch: `$env:PWSH_SHIM_BYPASS = "1"`.

**Q: What about PowerShell 7 (pwsh)?**  
A: The shim targets `powershell.exe` (PS 5.1). If your IDE uses `pwsh.exe`, only the profile layer applies.

**Q: Why not just change the IDE's shell?**  
A: Most IDE agent frameworks hardcode `powershell -Command` on Windows. There's no setting to change this.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

[MIT](LICENSE)
