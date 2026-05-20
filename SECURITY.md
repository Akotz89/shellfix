# Security Policy

## Scope

shellfix intercepts `powershell -Command` calls and routes them to either WSL bash or real PowerShell. It runs with the same permissions as the calling process (typically the IDE).

## Security Considerations

- The shim is a compiled executable placed in your PATH. Verify the binary matches the source before installing.
- The `-File` fallback writes temporary `.ps1` scripts to `%TEMP%`. These are deleted immediately after execution.
- The shim does not make network requests, store credentials, or access files beyond what the intercepted command accesses.
- The profile wraps native tools by merging stderr to stdout. This does not suppress actual errors — exit codes are preserved.

## Reporting Vulnerabilities

If you find a security issue, please email the maintainer directly rather than opening a public issue.

Contact: Open a private issue on the repository or reach out via GitHub profile.

## Supported Versions

| Version | Supported |
|---|---|
| 1.1.x | Yes |
| 1.0.x | No (missing Class 2 and 3 defenses) |
