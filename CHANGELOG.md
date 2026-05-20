# Changelog

All notable changes to this project will be documented in this file.

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
- Prevents garbled `[31m` text that confuses agents into thinking commands failed

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

Discovered during real-world usage that bash→WSL routing (Class 1) was only one of three distinct failure modes agents hit. Added fixes for Classes 2 and 3.

#### Class 2: Complex PS Quoting → `-File` Fallback
- Added `HasDangerousQuoting()` heuristic to detect multi-line commands, unbalanced quotes, and mixed quoting patterns that break PS 5.1's `-Command` parser
- Added `RunPsViaFile()` which writes commands to a temp `.ps1` file and runs with `-File`, completely bypassing argument parsing
- Temp files are cleaned up automatically after execution

#### Class 3: NativeCommandError Suppression
- Profile now wraps `git`, `npm`, `npx`, `dotnet`, `gh`, `cargo`, `rustc`, `docker`, and `kubectl` in functions that merge stderr to stdout as plain strings
- Prevents PS 5.1 from treating normal stderr output (progress, warnings, diagnostics) as errors (red text)
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
- Path translation: Windows → WSL with space-safe quoting
- Apostrophe escaping (`'` → `\'`) — fixes infinite hang on `grep "it's"`
- Dollar sign preservation (`$` → `\$`) — fixes awk/bash variable expansion
- Glob re-quoting (`-name *.py` → `-name '*.py'`) — fixes find pattern expansion
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
