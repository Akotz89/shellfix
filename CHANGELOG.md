# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added — Commercial installer management CLI
- Added `shellfix.exe` as the primary management surface for `install`, `uninstall`, `status`, `doctor`, `repair antigravity`, and `test`.
- Added `%LOCALAPPDATA%\Programs\Shellfix\` as the product install root while keeping `%USERPROFILE%\bin\powershell.exe` as the compatibility shim path.
- Added `%LOCALAPPDATA%\Shellfix\state.json` and backup tracking for reversible profile, shortcut, PATH, and Antigravity settings changes.
- Replaced the large PowerShell installer body with a compatibility bootstrapper that builds or locates `shellfix.exe` and forwards legacy flags.
- Organized CLI code into command, service, model, utility, and test-support files instead of one large entrypoint.
- Release and CI workflows now build and publish both `powershell.exe` and `shellfix.exe`.

### Added — Antigravity IDE agent-shell settings hardening
- Installer now merges Antigravity IDE user settings when `settings.json` exists.
- Adds a `shellfix` terminal profile pointing at the installed shim.
- Sets `terminal.integrated.agentHostProfile.windows`, `terminal.integrated.automationProfile.windows`, and `terminal.integrated.defaultProfile.windows` to Shellfix.
- Antigravity IDE is now settings-managed: Shellfix leaves Antigravity shortcuts as direct `Antigravity IDE.exe` shortcuts and removes stale launcher sidecars on reinstall.
- Adds `install.ps1 -TestAntigravitySettings` for an idempotent temp-file merge test and `-SkipAntigravitySettings` to opt out.
- `shellfix doctor` now reports live Antigravity PowerShell child processes that bypass the installed shim, which catches stale terminals/windows opened before repair or reinstall.

### Fixed — Agent-first WSL command shapes
- Explicit `wsl` / `wsl.exe` commands now route directly through the shim before PowerShell can parse nested bash, Python, JSON, heredoc, or `$PATH` payloads.
- Session proxy mode buffers WSL heredoc stdin payloads and pipes the body to WSL stdin, covering `wsl ... -- python3 << 'PY' ... PY` and Node equivalents.
- Added incident route fixtures and replay coverage for WSL/bash multiline Python `-c`, JSON payloads, heredoc stdin, native inline Python/Node, native stderr false failures, and stale Antigravity shell bypass detection.

### Fixed — pwsh 7 proxy quoting regression
- Session proxy no longer injects `--%` into problematic WSL commands when the backend is PowerShell 7.
- Added regression coverage for `&&` plus Python slice payloads in `wsl ... bash -c`.
- Updated smoke/proxy tests for current debug output and PS 5.1/7 backend behavior.

### Fixed — Native inline interpreter reliability
- The shim now keeps installed Windows developer tools native-first, including `python`, `python3`, `py`, `node`, `npm`, and `npx`.
- `python -c`, `python3 -c`, `py -c`, and `node -e` payloads are written to temporary script files and executed directly so PowerShell never parses the inline code body.
- Session proxy mode now buffers multiline native inline payloads before execution, fixing Antigravity fallback cases where multiline Python was parsed as PowerShell.
- `shellfix doctor` now reports resolved native paths for `python`, `python3`, `node`, and `npx`.
- The profile refreshes PATH from persisted User/Machine environment entries so newly installed tools such as Winget-installed D2 are visible without restarting the IDE.
- `d2` is wrapped as a native tool so its `success:`/`info:` stderr messages are normalized instead of looking like command failures to agents.

---

## [1.7.1] — 2026-05-20

### Fixed — Shortcut patching quotes and restore path (OPE-116 / #13)
- Shortcut patching now generates a per-shortcut launcher script instead of embedding IDE paths and existing arguments inside `cmd.exe /C ... && start ...`.
- Existing shortcut arguments are preserved in the launcher and replayed through `ShellExecute`.
- `install.ps1 -Uninstall` restores sidecar `.shellfix-backup` files and removes generated launcher scripts.
- `install.ps1 -TestShortcuts` adds a synthetic patch/launch/restore verification path with spaces, parentheses, ampersands, and quoted arguments.

---

## [1.7.0] — 2026-05-20

### Changed — Profile install is now non-destructive (OPE-110 / #7)
- Installer no longer overwrites the user's PowerShell profile
- shellfix profile is installed as a separate `shellfix_profile.ps1` snippet
- A guarded dot-source block (`# >>> shellfix >>>`) is injected into the user profile
- Reinstall is idempotent — the block is detected and replaced in place
- Full `-Uninstall` support: removes the block, deletes snippet, removes shim, restores shortcuts

### Added — Release checksums and trust documentation (OPE-113 / #10)
- Release workflow generates `checksums.txt` with SHA256 hashes for all assets
- SECURITY.md rewritten with comprehensive trust model documentation:
  - PATH-shadowing risk explanation
  - Checksum verification steps (PowerShell commands)
  - Build-from-source instructions
  - Code-signing roadmap
- README updated with verification and uninstall instructions

---

## [1.6.0] — 2026-05-20

### Fixed — Release-blocking issues found in user testing

#### Bug 1: Native dev tools routed to WSL instead of Windows
`git`, `npm`, `node`, `npx`, `docker`, `kubectl`, `cargo`, `rustc`, `make`,
`gcc`, `g++` were listed in the shim's `bashCommands` array. This caused:
- `npm --version` resolved inside WSL and failed when npm was only installed on Windows
- `git status --short` ran WSL Git and reported the Windows checkout incorrectly

**Fix:** Removed all Windows-native dev tools from the bash routing list. They
now pass through to real PowerShell where the profile wraps them with
NativeCommandError suppression and ANSI stripping.

#### Bug 2: Release packaging doesn't match install instructions
GitHub Release assets are flat files (`powershell.exe`, `install.ps1`, etc.)
but `install.ps1 -SkipBuild` expected `shim\out\powershell.exe` repo layout.

**Fix:** Installer now checks both locations — repo layout first, then
same-directory (flat release download). Clear error message if neither found.

#### Bug 3: Hardcoded user path in profile
`Test-ShimPath` had `C:\Users\Aaron\bin\powershell.exe` hardcoded.

**Fix:** Replaced with `Join-Path $env:USERPROFILE "bin\powershell.exe"`.

#### Bug 4: Test harness quoting bug (3/48 failures)
Issue regression tests used `& $shimPath -Command $Command` which causes PS
to re-parse the command before the shim sees it, mangling quotes and `&&`.

**Fix:** Switched to `Start-Process -ArgumentList` which preserves the raw
command string.

---

## [1.5.2] — 2026-05-20

### Added — IDE Shortcut Patching for `run_command` Interception

Discovered that VS Code-based IDEs' agent `run_command` tool spawns bare
`powershell` (no full path) via Go's `exec.LookPath`, which searches `%PATH%`
in order. The shim wasn't intercepting because `System32\WindowsPowerShell\v1.0`
comes before the user's bin directory in the merged PATH.

#### Installer-Driven Shortcut Patching
- `install.ps1` now auto-detects installed IDEs: VS Code, VS Code Insiders,
  Cursor, Windsurf, Antigravity IDE
- Patches desktop and Start Menu shortcuts to prepend the shim directory to PATH
- Creates `.shellfix-backup` files for each patched shortcut
- `install.ps1 -Uninstall` restores all shortcuts from backups
- New `-SkipShortcuts` flag to skip this step if not wanted

#### Generic Launcher
- Added `launch-ide.bat` — generic launcher that accepts any IDE exe as argument
- Removed hardcoded `launch-antigravity.bat/vbs` in favor of the generic approach

#### Key Research Findings
- `run_command` is NOT controlled by `agentHostProfile`, `automationProfile`,
  or `tools.shell.executable` — the Go language server binary hardcodes bare `powershell`
- No `-NoProfile` is passed — the PowerShell profile loads on every `run_command`
- IFEO (Image File Execution Options) registry redirect was rejected as too dangerous
  (system-wide, AV flags it, MITRE ATT&CK T1546.012)

### Tests
- Added `test-replay.ps1` — 9 tests replaying actual historical session failures
  (heredocs, python slices, `&&` chains, `curl | python` pipes)
- Both `test-proxy.ps1` (16 tests) and `test-replay.ps1` (9 tests) pass

---

## [1.5.1] — 2026-05-20

### Fixed — BOM Stripping in Session Proxy

The Antigravity IDE injects a UTF-8 BOM (`EF BB BF`) into stdin when piping
commands to `run_command`. .NET's `Console.ReadLine()` with default CP437 encoding
misinterprets this as `ï»¿` (U+FEFF), which gets prepended to the first token of
every command, breaking detection logic.

#### BOM Fix
- Set `Console.InputEncoding = new UTF8Encoding(false)` in `RunInteractiveProxy`
- Added `line.TrimStart('\uFEFF')` to strip any residual BOM character
- Both fixes are defense-in-depth — either one alone would work

---

## [1.5.0] — 2026-05-20

### Added — Session Proxy Mode

v1.4.0's one-shot `-Command` interception was never invoked by IDE agents, which
use `terminal.sendText()` to inject commands into a persistent PS session via
stdin. v1.5.0 adds a session proxy that actually solves the problem.

#### Session Proxy (`RunInteractiveProxy`)
- Shim spawns real `powershell.exe` as a child process with stdin redirected
- Reads each stdin line and inspects it via `RewriteForProxy()`
- WSL commands containing problematic tokens (`&&`, `||`, `[N:-N]`, nested bash
  quotes) are rewritten to inject the `--%` stop-parsing token
- Pure PowerShell commands pass through unchanged
- `PWSH_SHIM_BYPASS=1` environment variable prevents infinite recursion

#### Problematic Token Detection (`HasProblematicTokens`)
- `&&` and `||` — PS 5.1 pipeline chain operators
- `[N:-N]` — PS interprets as array index/slice
- Nested single quotes inside double quotes in `bash -c` patterns

#### IDE Configuration
- Added `shellfix` terminal profile to Antigravity IDE `settings.json`
- Set as `agentHostProfile` so the agent's terminal uses the shim

### Changed
- Removed unused `LooksLikeBashWithProblematicTokens()` and `EscapeForBashC()`
  (eliminated CS8321 compiler warnings)
- Bare-bash rewrite path disabled — too aggressive, caused false positives on
  PS `echo` commands containing `&&` inside string literals

### Tests
- New `test-proxy.ps1` — 16 tests covering all three issues plus regression
- Tests use `Process.Start` + stdin injection to simulate IDE behavior

---

## [1.4.0] — 2026-05-20 *(superseded by 1.5.0)*

### Fixed — Raw Command Line Extraction (Issues #1, #2, #3)

The shim now reads the **raw command line** from `Environment.CommandLine` instead
of relying on `args[]` which are pre-tokenized by PowerShell. This means tokens
like `&&`, `[1:-1]`, and nested single quotes never reach PS's parser.

#### Issue #1: `&&` in `wsl bash -c` (Critical)
- PowerShell 5.1 rejects `&&` as an invalid statement separator
- Fix: shim extracts raw `-Command` payload before PS tokenizes it
- WSL-prefix commands (`wsl -d ... -- bash -c "..."`) now pass through directly

#### Issue #2: Python `[1:-1]` slice syntax
- PS interprets `[1:-1]` as a malformed array index expression
- Fix: raw command line extraction bypasses PS bracket parsing

#### Issue #3: Nested single quotes with `curl | python`
- Multi-layer quoting across PowerShell, bash, curl, and Python caused EOF errors
- Fix: `ParseCommandArgs()` respects double/single quoted strings in passthrough

#### Issue #4: Heredoc documentation (Docs)
- Added "Known Interactions" section to README documenting the heredoc pattern
  for embedding Python/Ruby/Perl in bash scripts created by AI tools

### Added
- `IsAlreadyWslWrapped()` — detects commands already wrapped in `wsl -d ... --`
- `RunWslPassthrough()` — passes WSL commands directly without re-wrapping
- `ParseCommandArgs()` — quote-aware argument parser for raw command strings
- WSL-prefix early exit in `LooksLikeBash()` classifier
- 4 new regression test cases for issues #1-#3

### Changed
- `Environment.CommandLine` is now the primary source for `-Command` extraction
- `args[]` is kept as fallback for non-standard invocations

---

## [1.3.0] — 2026-05-20

### Added — Tier 2 Fixes

#### BOM-Safe File Writing
- Default `Set-Content`, `Out-File`, `Add-Content` to UTF-8 encoding via `PSDefaultParameterValues`
- New `Write-Utf8NoBom` helper function for truly BOM-free writes using .NET
- Prevents UTF-16LE null-byte corruption and unwanted BOM in JSON/YAML/config files

#### Long Path Support
- Installer checks `HKLM:\...\FileSystem\LongPathsEnabled` registry key
- Auto-enables if possible (requires admin), warns with manual command if not
- Fixes "path too long" errors with deep `node_modules` trees

#### Shell Integration Compatibility
- Profile detects VS Code (`TERM_PROGRAM=vscode`) and avoids redefining the prompt
- Prevents infinite loops and hangs caused by conflicting prompt functions
- Sets a minimal prompt outside VS Code that doesn't conflict with IDE markers

### Test Suite
- 39 → 44 tests (BOM verification, encoding defaults, prompt function, shell compat)

---

## [1.2.0] — 2026-05-20

### Added — Community Pain Point Fixes (Tier 1)

Based on extensive research across Cursor, Windsurf, Copilot, and Reddit forums.

#### ANSI Escape Code Suppression
- Profile sets `NO_COLOR=1` and `TERM=dumb` to suppress color codes at the source
- All native tool wrappers now strip remaining ANSI escape sequences via regex
- Prevents raw ANSI sequences such as `[31m` from being interpreted as failure output

#### dotnet Terminal Logger Auto-Disable
- `dotnet` wrapper auto-injects `--tl:off` for `build`, `test`, `run`, `publish`, `pack`, `restore`
- .NET 8+'s Terminal Logger uses ANSI cursor movement that agents can't parse
- Also sets `DOTNET_NOLOGO=1` and `DOTNET_CLI_TELEMETRY_OPTOUT=1`

#### ExecutionPolicy Auto-Fix
- Installer checks `ExecutionPolicy` for `CurrentUser` scope
- Automatically sets to `RemoteSigned` if `Restricted` or `Undefined`
- Prevents "running scripts is disabled on this system" errors for agent temp scripts

#### Output Truncation Fix
- Sets `$FormatEnumerationLimit = -1` to prevent `...` truncation in collections
- Sets `$PSDefaultParameterValues['Format-Table:AutoSize'] = $true` for full-width tables
- Agents no longer make decisions based on incomplete output

### Test Suite
- 34 → 39 tests (added ANSI strip, env vars, formatting)

---

## [1.1.0] — 2026-05-20

### Added — Two New Failure Classes

Real-world usage showed that bash-to-WSL routing (Class 1) was only one of three distinct failure modes agents hit. Added fixes for Classes 2 and 3.

#### Class 2: Complex PS Quoting to `-File` Fallback
- Added `HasDangerousQuoting()` heuristic to detect multi-line commands, unbalanced quotes, and mixed quoting patterns that break PS 5.1's `-Command` parser
- Added `RunPsViaFile()` which writes commands to a temp `.ps1` file and runs with `-File`, completely bypassing argument parsing
- Temp files are cleaned up automatically after execution

#### Class 3: NativeCommandError Suppression
- Profile now wraps `git`, `npm`, `npx`, `dotnet`, `gh`, `cargo`, `rustc`, `docker`, and `kubectl` in functions that merge stderr to stdout as plain strings
- Prevents PS 5.1 from styling normal stderr output (progress, warnings, diagnostics) as command failure output
- Exit codes still propagate correctly via `$LASTEXITCODE`

### Changed
- Renamed repo from `wsl-shell-hardening` to `shellfix`
- README rewritten to document all three failure classes
- Architecture diagram updated to show three routing paths

### Infrastructure
- Added GitHub Actions CI (build + artifact upload)
- Added CONTRIBUTING.md
- Added `.editorconfig`
- Added GitHub topics for discoverability

---

## [1.0.0] — 2026-05-20

### Initial Release — Class 1: Bash Routing

Two-layer defense system for running bash commands through Windows PowerShell terminals.

#### Layer 1: C# Shim
- Heuristic classifier (100+ bash commands, PS verb-noun detection, syntax markers)
- Path translation: Windows to WSL with space-safe quoting
- Apostrophe escaping (`'` to `\'`) fixes unmatched quotes in commands such as `grep "it's"`
- Dollar sign preservation (`$` to `\$`) fixes awk/bash variable expansion
- Glob re-quoting (`-name *.py` to `-name '*.py'`) fixes early glob expansion
- WSL crash fallback to real PowerShell
- WSL_UTF8 environment enforcement
- Kill switch and debug mode

#### Layer 2: PowerShell Profile
- 50+ bash command wrappers with pipeline support
- Conflicting alias removal (curl, diff, sort, cat, etc.)
- Per-argument path translation and quoting
- WSLENV passthrough for common environment variables
- WSL health check function
- UTF-8 no-BOM encoding throughout
