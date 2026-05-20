# Security Policy

## Scope

shellfix intercepts PowerShell invocations and either routes them (one-shot mode) or proxies stdin (session proxy mode). It runs with the same permissions as the calling process (typically the IDE).

## Security Considerations

- The shim is a compiled executable configured as the IDE's terminal shell. Verify the binary matches the source before installing.
- In session proxy mode, the shim spawns real `powershell.exe` as a child process and forwards stdin. Only WSL commands with specific problematic tokens are rewritten; all other input passes through unchanged.
- The `-File` fallback writes temporary `.ps1` scripts to `%TEMP%`. These are deleted immediately after execution.
- The `PWSH_SHIM_BYPASS=1` environment variable prevents infinite recursion when the shim spawns child PS processes.
- The shim does not make network requests, store credentials, or access files beyond what the intercepted command accesses.
- The profile wraps native tools by merging stderr to stdout as plain strings. This does not suppress actual errors — exit codes are preserved.

## Reporting Vulnerabilities

If you find a security issue, please email the maintainer directly rather than opening a public issue.

Contact: Open a private issue on the repository or reach out via GitHub profile.

## Supported Versions

| Version | Supported |
|---|---|
| 1.5.x | Yes (current) |
| 1.3.x–1.4.x | Partial — one-shot mode only, no session proxy |
| 1.2.x | Partial — Tier 1 fixes only |
| 1.1.x | Partial — missing Tier 1/2 features |
| 1.0.x | No (missing Class 2, 3, and proxy defenses) |
