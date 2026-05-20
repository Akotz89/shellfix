# wsl-shell-hardening

**Make Windows PowerShell stop breaking your bash commands.**

A two-layer defense system that lets AI coding agents (and humans) run bash commands transparently from Windows PowerShell terminals. No more quoting nightmares, no more path translation failures, no more `$` expansion eating your awk patterns.

---

## The Problem

AI coding agents (Cursor, Windsurf, GitHub Copilot, Antigravity, etc.) run commands through PowerShell on Windows. When they emit bash commands like:

```bash
grep -c "def " "C:\Users\Aaron\My Project\app.py"
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
| `C:\Users\Aaron\My Project\file.py` | ❌ Path not found | ✅ → `/mnt/c/Users/Aaron/My Project/file.py` |

## Architecture

```
┌──────────────────────────────────────────────────┐
│  IDE / Agent calls: powershell -Command "..."    │
└─────────────────────┬────────────────────────────┘
                      │
         ┌────────────▼────────────────┐
         │  Layer 1: C# Shim (v4)     │
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
4. **Routes** bash to `wsl.exe -d Ubuntu-24.04 -- bash -c` via `.NET ArgumentList` (no shell quoting layer)
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
- .NET 8 SDK (for building the shim)
- PowerShell 5.1+ (comes with Windows)

## Installation

### Quick Install

```powershell
# Clone
git clone https://github.com/Akotz89/wsl-shell-hardening.git
cd wsl-shell-hardening

# Run installer
.\install.ps1
```

### Manual Install

```powershell
# 1. Build the shim
cd shim
dotnet publish -c Release -o out

# 2. Create bin directory and add to PATH
mkdir "$env:USERPROFILE\bin" -ErrorAction SilentlyContinue
Copy-Item out\powershell.exe "$env:USERPROFILE\bin\powershell.exe"

# 3. Add to PATH (before System32)
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($userPath -notmatch 'bin') {
    [Environment]::SetEnvironmentVariable('Path', "$env:USERPROFILE\bin;$userPath", 'User')
}

# 4. Install the profile
Copy-Item ..\profile\Microsoft.PowerShell_profile.ps1 `
    "$env:USERPROFILE\Documents\WindowsPowerShell\Microsoft.PowerShell_profile.ps1"
```

### Verify Installation

```powershell
# Check shim is first
where.exe powershell.exe
# Should show: C:\Users\<you>\bin\powershell.exe first

# Run tests
.\test.ps1
```

## Configuration

### WSL Distribution

Edit `shim/PowerShellShim.cs` line 22:
```csharp
const string WslDistro = "Ubuntu-24.04";  // Change to your distro
```

### Controls

| Control | How |
|---|---|
| **Disable shim** | `$env:PWSH_SHIM_BYPASS = "1"` |
| **Debug mode** | `$env:PWSH_SHIM_DEBUG = "1"` |
| **Uninstall** | Delete `~\bin\powershell.exe` and remove profile |

### Adding Commands

To add more commands to the profile wrapper list, edit the `$wslCommands` array in the profile.

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
C:\Users\Aaron\My Project\file.py
  → '/mnt/c/Users/Aaron/My Project/file.py'
     (single-quoted because it contains spaces)

C:\Users\Aaron\code\app.py
  → /mnt/c/Users/Aaron/code/app.py
     (no quotes needed)

\\wsl.localhost\Ubuntu-24.04\home\aaron\file
  → /home/aaron/file
```

## Testing

Run the included test suite:

```powershell
.\test.ps1
```

This runs 23+ tests covering:
- Core bash commands with path translation
- Apostrophes, dollar signs, double quotes, globs
- Control flow (`for`, `&&`, `if/then/fi`)
- Pipeline chains (bash→bash, PS→bash, bash→PS)
- Exit code propagation
- PowerShell passthrough
- WSL health checks

## FAQ

**Q: Does this work with Cursor/Windsurf/Copilot?**  
A: Yes. Any tool that calls `powershell -Command "..."` (which is all of them on Windows) will benefit from both layers.

**Q: Will this break my normal PowerShell usage?**  
A: No. The shim only activates on `-Command` invocations. Interactive PowerShell sessions load the profile, which adds bash wrappers but doesn't remove any PS functionality. The kill switch (`PWSH_SHIM_BYPASS=1`) disables the shim entirely.

**Q: What about PowerShell 7 (pwsh)?**  
A: The shim targets `powershell.exe` (PS 5.1) which is what most IDEs use. If your IDE uses `pwsh.exe`, the profile still works but the shim would need to be renamed.

**Q: Does this work without WSL?**  
A: No. This project bridges PowerShell to WSL. Without WSL, bash commands will fail (gracefully — the shim falls back to PowerShell).

**Q: Why not just change the IDE's terminal to bash?**  
A: Most IDE agent frameworks hardcode `powershell -Command` on Windows. There's no setting to change this in Cursor, Windsurf, or Antigravity as of 2026.

## License

MIT License. See [LICENSE](LICENSE).
