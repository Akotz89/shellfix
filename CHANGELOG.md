# Changelog

## [1.0.0] — 2026-05-20

### Initial Release

Two-layer defense system for running bash commands through Windows PowerShell terminals.

#### Layer 1: C# Shim (v4)
- Heuristic classifier (100+ bash commands, PS verb-noun detection, syntax markers)
- Path translation: Windows → WSL with space-safe quoting
- Apostrophe escaping (`'` → `\'`) — fixes infinite hang on `grep "it's"`
- Dollar sign preservation (`$` → `\$`) — fixes awk/bash variable expansion
- Glob re-quoting (`-name *.py` → `-name '*.py'`) — fixes find pattern expansion
- WSL crash fallback to real PowerShell
- WSL_UTF8 environment enforcement
- Kill switch and debug mode

#### Layer 2: PowerShell Profile (v10)
- 50+ bash command wrappers with pipeline support
- Conflicting alias removal (curl, diff, sort, cat, etc.)
- Per-argument path translation and quoting
- WSLENV passthrough for common environment variables
- WSL health check function
- Shim PATH verification function
- UTF-8 no-BOM encoding throughout
