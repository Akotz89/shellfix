<p align="center">
  <img src="assets/shellfix-wordmark.svg" alt="Shellfix - Stop PowerShell from breaking AI agent commands" width="960">
</p>

[![CI](https://github.com/Akotz89/shellfix/actions/workflows/ci.yml/badge.svg)](https://github.com/Akotz89/shellfix/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Windows](https://img.shields.io/badge/Platform-Windows-0078D6.svg)](https://github.com/Akotz89/shellfix)

**Stop PowerShell from breaking AI agent commands.**

Shellfix is a Windows command shim for AI coding agents and terminal-heavy developer workflows. It reduces PowerShell quoting, WSL routing, path translation, stderr, ANSI, and UTF-8 friction so commands behave closer to what the caller intended.

### Quick Start

```powershell
git clone https://github.com/Akotz89/shellfix.git
cd shellfix
.\install.ps1    # builds, installs, patches your IDE shortcuts
.\test.ps1       # verifies the repo or installed shim
# Restart your IDE → done
```

> **Pre-built binary?** Download `powershell.exe`, `install.ps1`, `Microsoft.PowerShell_profile.ps1`, `launch-ide.bat`, and `checksums.txt` from [Releases](https://github.com/Akotz89/shellfix/releases). Place them in one folder, then run `.\install.ps1 -SkipBuild`.
>
> **Verify checksums:** `Get-FileHash powershell.exe -Algorithm SHA256` — compare with `checksums.txt` in the release.
>
> **Uninstall:** `.\install.ps1 -Uninstall` removes the shim, profile snippet, and restores IDE shortcuts.

---

## The Problem

AI coding agents and terminal-heavy Windows workflows often run commands through PowerShell. Three classes of failures show up repeatedly:

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

Agents and automation can misread that output as failure and retry with unnecessary quoting or subprocess workarounds.

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
| `dotnet build` | ❌ Garbled Terminal Logger output | ✅ Auto `--tl:off` |
| Any command output | ❌ ANSI codes: `[31m` garble | ✅ Stripped clean |
| `Format-Table` output | ❌ Truncated with `...` | ✅ Full width |
| `Set-Content "file"` | ❌ UTF-16LE / BOM corruption | ✅ UTF-8 no-BOM |
| Deep `node_modules` paths | ❌ 260-char MAX_PATH failure | ✅ LongPathsEnabled |

## Architecture

```mermaid
flowchart TD
    IDE["IDE / Agent"]
    IDE --> |"powershell -Command '...'"| ONESHOT
    IDE --> |"terminal.sendText via stdin"| PROXY

    subgraph SHIM["Layer 1: C# Shim (powershell.exe)"]
        direction TB
        ONESHOT["One-Shot Mode"]
        PROXY["Session Proxy Mode"]
        CLS["Heuristic Classifier"]
        REWRITE["Stdin Rewriter"]
        ONESHOT --> CLS
        PROXY --> REWRITE
        CLS --> |"Bash syntax"| BASH
        CLS --> |"Complex quoting"| FILE
        CLS --> |"Simple PS"| PASS
        REWRITE --> |"WSL + problematic tokens"| INJECT["Inject --% stop-parsing"]
        REWRITE --> |"Safe command"| PASSTHRU["Pass through"]
    end

    BASH["WSL bash -c<br/>Path translation<br/>Quote/glob escaping<br/>Dollar-sign preservation"]
    FILE["-File fallback<br/>Write temp .ps1<br/>Bypass PS argument parser"]

    subgraph REAL["Real powershell.exe + Profile (Layers 2 and 3)"]
        direction TB
        L2["Layer 2: Bash Wrappers<br/>50+ commands, pipeline support"]
        L3["Layer 3: Environment and Tool Wrappers<br/>NativeCommandError suppression<br/>ANSI stripping, BOM-safe writes<br/>dotnet --tl:off, UTF-8"]
    end

    PASS --> REAL
    INJECT --> REAL
    PASSTHRU --> REAL

    style IDE fill:#1a1a2e,stroke:#e94560,color:#eee
    style SHIM fill:#16213e,stroke:#0f3460,color:#eee
    style ONESHOT fill:#0f3460,stroke:#53779a,color:#eee
    style PROXY fill:#0f3460,stroke:#53779a,color:#eee
    style CLS fill:#0f3460,stroke:#53779a,color:#eee
    style REWRITE fill:#0f3460,stroke:#53779a,color:#eee
    style INJECT fill:#1a472a,stroke:#2d6a4f,color:#eee
    style PASSTHRU fill:#16213e,stroke:#0f3460,color:#eee
    style BASH fill:#1a472a,stroke:#2d6a4f,color:#eee
    style FILE fill:#4a3728,stroke:#8b6914,color:#eee
    style PASS fill:#16213e,stroke:#0f3460,color:#eee
    style REAL fill:#1b1b3a,stroke:#6c63ff,color:#eee
    style L2 fill:#2d2d5e,stroke:#6c63ff,color:#eee
    style L3 fill:#2d2d5e,stroke:#6c63ff,color:#eee
```

### Layer 1: Compiled C# Shim

A .NET 8 executable named `powershell.exe` configured as the IDE's terminal shell. It operates in two modes:

**One-Shot Mode** (`powershell -Command "..."`): The shim classifies the command:
1. **Bash syntax** → escapes quotes, translates paths, routes to `wsl.exe -- bash -c`
2. **Complex PS quoting** → writes to a temp `.ps1` file, runs with `-File` (bypasses parser)
3. **Simple PS** → passes through to real `powershell.exe`
4. **Falls back** to real PowerShell if WSL crashes

**Session Proxy Mode** (interactive terminal / `terminal.sendText`): The shim spawns real `powershell.exe` as a child process and proxies stdin line-by-line. Each line is inspected:
1. If it starts with `wsl`/`wsl.exe` and contains problematic tokens (`&&`, `||`, `[N:-N]`, nested bash quotes) → rewrites with `--%` stop-parsing token
2. Otherwise → passes through unchanged

### Layer 2: PowerShell Profile — Bash Wrappers

Creates function wrappers for 50+ bash commands that handle path translation, quoting, dollar-sign escaping, and pipeline support.

### Layer 3: PowerShell Profile — Environment & Native Tool Wrappers

When the shellfix profile is loaded, wraps `git`, `npm`, `npx`, `dotnet`, `gh`, `cargo`, `rustc`, `docker`, and `kubectl` in functions that:

- Merge stderr to stdout as plain strings (prevents NativeCommandError red text)
- Strip ANSI escape codes from output (prevents `[31m` garbled text)
- Inject `--tl:off` for dotnet build/test/run/publish (disables Terminal Logger)
- Set `NO_COLOR=1` and `TERM=dumb` environment variables
- Set `$FormatEnumerationLimit = -1` to prevent output truncation
- Default `Set-Content`, `Out-File`, `Add-Content` to UTF-8 (prevents UTF-16LE/BOM corruption)
- Provide `Write-Utf8NoBom` helper for truly BOM-free file writes
- Guard against VS Code shell integration prompt conflicts
- Preserve real exit codes via `$LASTEXITCODE`

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

### Pre-Built Release

Download these files from a [GitHub Release](https://github.com/Akotz89/shellfix/releases) into the same folder:

- `powershell.exe`
- `install.ps1`
- `Microsoft.PowerShell_profile.ps1`
- `launch-ide.bat`
- `checksums.txt`

Verify the binary before installing:

```powershell
Get-FileHash .\powershell.exe -Algorithm SHA256
# Compare the hash with the powershell.exe line in checksums.txt
```

Then install without rebuilding:

```powershell
.\install.ps1 -SkipBuild
```

### Verify

```powershell
.\test.ps1
```

For a narrower check that matches CI:

```powershell
.\test-ci-smoke.ps1 -ShimPath .\shim\out\powershell.exe
```

## Configuration

### WSL Distribution

```powershell
.\install.ps1 -WslDistro "Ubuntu-22.04"
```

The installer stores this as the user environment variable `SHELLFIX_WSL_DISTRO`.
Both the shim and the PowerShell profile read that same value at runtime, so
pre-built release binaries do not need to be rebuilt for a different distro.

### Controls

| Control | How |
|---|---|
| **Disable shim** | `$env:PWSH_SHIM_BYPASS = "1"` |
| **Debug mode** | `$env:PWSH_SHIM_DEBUG = "1"` |
| **Override WSL distro** | `$env:SHELLFIX_WSL_DISTRO = "Ubuntu-22.04"` |
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
.\test-ci-smoke.ps1  # CI smoke tests (5 checks)
.\test.ps1           # Full one-shot/profile suite (48 tests)
.\test-proxy.ps1     # Session proxy tests (16 tests)
.\test-replay.ps1    # Historical session replay (10 tests)
```

- `test-ci-smoke.ps1` covers the stable CI gate: PowerShell passthrough, native tool passthrough policy, explicit WSL, WSL `&&`, and Python slice syntax
- `test.ps1` covers all failure classes (bash routing, quoting, NativeCommandError) plus Tier 1/2 features
- `test-proxy.ps1` covers the session proxy mode: `&&`, `[N:-N]`, nested quotes, and pure PS regression
- `test-replay.ps1` replays actual historical failures from real agent sessions (heredocs, python slices, curl pipes)

### Verifying Runtime Modes

Use these checks after install, especially when debugging an IDE or agent process tree.

**One-shot mode (`powershell -Command`)**

```powershell
.\test-ci-smoke.ps1 -ShimPath "$env:USERPROFILE\bin\powershell.exe"
```

Expected: PowerShell passthrough, native passthrough policy, and WSL smoke checks pass. WSL checks skip only if the configured distro is unavailable.

**Session proxy mode (`terminal.sendText` / interactive stdin)**

```powershell
.\test-proxy.ps1 -ShimPath "$env:USERPROFILE\bin\powershell.exe"
```

Expected: proxy tests pass for WSL `&&`, Python slice syntax, nested quotes, and normal PowerShell commands.

**`-File` fallback mode**

This path is used only for PowerShell commands that the shim classifies as dangerous for `-Command` quoting. It is not used for normal WSL routing or simple PowerShell passthrough.

```powershell
$env:PWSH_SHIM_DEBUG = "1"
& "$env:USERPROFILE\bin\powershell.exe" -NoProfile -Command 'Write-Output "fallback ''quote'' $env:USERNAME"'
Remove-Item Env:PWSH_SHIM_DEBUG
```

Expected debug lines include `Dangerous quoting detected, using -File mode` and `Wrote temp script: ...\shellfix_<id>.ps1`, followed by the command output.

## FAQ

**Q: Does this work with Cursor / Windsurf / Copilot / Antigravity?**  
A: Yes. Both one-shot (`-Command`) and interactive (stdin) invocations are handled. Configure the IDE's terminal profile to point to the shim binary.

**Q: Will this break my normal PowerShell?**  
A: No. Kill switch: `$env:PWSH_SHIM_BYPASS = "1"`. Pure PS commands pass through unchanged.

**Q: Why do I still see red text sometimes?**  
A: NativeCommandError cleanup is profile-layer behavior. It applies when the shellfix profile is loaded and only for tools in the wrapper list (`git`, `npm`, `gh`, etc.). Commands launched with `-NoProfile`, or tools outside that list, can still show normal PowerShell stderr behavior.

**Q: What about PowerShell 7?**  
A: PS 7 fixes `&&`/`||` and NativeCommandError natively. The shim and bash wrappers still provide value for path translation and bash routing.

**Q: Why not just switch to bash/Git Bash?**  
A: Many IDE agent frameworks default to PowerShell on Windows. The shim lets them work without reconfiguring the agent itself.

## Known Limitations

- The profile-layer fixes require the PowerShell profile to load. If a caller uses `-NoProfile`, the shim still handles routing/classification, but profile functions such as native stderr cleanup, ANSI stripping, and `Write-Utf8NoBom` are not loaded.
- WSL routing requires the configured distro to exist and be running. Use `.\install.ps1 -WslDistro "<name>"` or set `SHELLFIX_WSL_DISTRO` when the default `Ubuntu-24.04` is not correct.
- Native tool cleanup is allowlisted. Tools outside `$nativeTools` can still emit stderr or ANSI output until added to the profile wrapper list.
- Shortcut patching affects the IDE process tree launched from the patched shortcut. Already-running IDE windows and shells launched from other shortcuts may keep their old PATH until restarted.
- The `shellfix_<id>.ps1` temp-file pattern appears only in debug output for `-File` fallback mode. It is not expected during normal one-shot WSL routing or session proxy rewrites.

## Known Interactions

### Embedding Python/Ruby/Perl in bash scripts (Issue #4)

AI agents frequently write bash scripts as workarounds for quoting issues. When these scripts contain inline Python with f-strings, backslash escaping can get mangled by the IDE's file-writing tool.

**Problem:** `write_to_file` may double-escape `\"` inside f-strings:
```python
# What the agent writes:
print(f"\n=== Score: {summary.get(\"score\",0)}% ===\n")
# What appears in the file:
print(f"\\n=== Score: {summary.get(\\\"score\\\",0)}% ===\\n")
```

**Solution:** Use single-quoted heredocs to embed Python in bash scripts:
```bash
#!/bin/bash
python3 - "$@" << 'PYEOF'
import json, sys
data = json.load(sys.stdin)
print(f"\nScore: {data.get('score', 0)}%\n")
PYEOF
```

Single-quoted heredoc markers (`<< 'PYEOF'`) pass content verbatim — no escaping layer applies.

### Session Proxy (v1.5.0+)

Since v1.5.0, the shim intercepts **both** one-shot (`-Command`) and interactive (stdin) invocations. When configured as the IDE's terminal shell, it spawns real `powershell.exe` as a child and proxies every stdin line through `RewriteForProxy()`. This means `&&`, `[1:-1]`, and nested quotes in WSL commands are fixed transparently — even when the IDE sends them via `terminal.sendText()` into a persistent session.

For this to work, the IDE must be configured to launch the shim binary as its terminal profile (not the system `powershell.exe`).

### Agent `run_command` Interception (v1.5.2+)

VS Code-based IDEs' agent `run_command` tool bypasses all terminal settings — it spawns bare `powershell` directly from the Go language server binary. The shim is only found if the shim directory comes before `C:\Windows\System32\WindowsPowerShell\v1.0\` in PATH.

**Automatic setup** — the installer handles this:
```powershell
.\install.ps1
# Detects VS Code, Cursor, Windsurf, Antigravity IDE
# Patches shortcuts to prepend shellfix to PATH
# Creates .shellfix-backup files for easy uninstall
```

**Manual setup** — if you prefer:

Use the generic `launch-ide.bat`:

```powershell
# Place launch-ide.bat in the same directory as the shim
launch-ide.bat "C:\path\to\IDE.exe" --your-args-here
```

For persistent shortcuts, prefer `.\install.ps1`. It creates a per-shortcut launcher script instead of embedding a fragile `cmd.exe /C ... && start ...` command in the shortcut target.

**How it works:** The IDE process tree inherits the modified PATH. When the language server calls bare `powershell`, Go's `exec.LookPath` finds the shim first. This has zero system-wide blast radius — only IDE child processes are affected.

**Supported IDEs:**
- Visual Studio Code / VS Code Insiders
- Cursor
- Windsurf
- Antigravity IDE
- Any VS Code-based IDE (manual shortcut patch)

**Verify it's working:** Run this from the agent:
```powershell
(Get-Command powershell.exe).Source
# Should point to C:\Users\<user>\bin\powershell.exe or your chosen shellfix BinDir
```

If that still points to `C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe`, restart the IDE from the patched shortcut or launch it through `launch-ide.bat`.

**Uninstall:** `.\install.ps1 -Uninstall` restores all shortcuts from backups.


## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
