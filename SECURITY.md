# Security Policy

## Trust Model

shellfix intentionally shadows `powershell.exe` in your PATH. This is a powerful and potentially risky operation — you are trusting this project's binary to intercept every PowerShell invocation from your IDE.

### What shellfix does

- Classifies incoming commands as bash or PowerShell
- Routes bash commands to WSL; passes PowerShell commands to the real `powershell.exe`
- In session proxy mode, spawns real `powershell.exe` and rewrites only WSL commands with problematic tokens (`&&`, `[N:-N]`, nested quotes)
- Writes temporary `.ps1` scripts to `%TEMP%` for complex PS commands (deleted immediately after execution)
- **Does not** make network requests, store credentials, or access files beyond what the intercepted command accesses

### What shellfix does NOT do

- It does not modify, log, or exfiltrate your commands or output
- It does not persist any data between invocations
- It does not run with elevated privileges (it inherits the IDE's permissions)

### PATH Shadowing Risk

The shim works by placing a `powershell.exe` binary earlier in PATH than `C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe`. This means:

- **Every** invocation of `powershell` or `powershell.exe` from your IDE will hit the shim first
- The `PWSH_SHIM_BYPASS=1` environment variable is the kill switch — set it to skip the shim entirely
- The installer creates shortcut backups in `~/.shellfix-backup` for rollback

## Verifying Release Binaries

Every GitHub Release includes a `checksums.txt` file with SHA256 hashes for all assets.

### Verification steps (PowerShell)

```powershell
# 1. Download the release assets
# 2. Verify the checksum matches
$expected = Get-Content checksums.txt | Where-Object { $_ -match 'powershell.exe' } | ForEach-Object { $_.Split(' ')[0] }
$actual = (Get-FileHash powershell.exe -Algorithm SHA256).Hash.ToLower()
if ($expected -eq $actual) { Write-Host "✓ Checksum matches" -ForegroundColor Green }
else { Write-Host "✗ CHECKSUM MISMATCH — do not use this binary" -ForegroundColor Red }
```

### Building from source

For maximum trust, build from source:

```powershell
git clone https://github.com/Akotz89/shellfix.git
cd shellfix
dotnet publish shim/PowerShellShim.csproj -c Release -o shim/out --nologo
# Verify: compare Get-FileHash shim/out/powershell.exe with your own build
```

### Code Signing

Release binaries are **not** currently code-signed. This is a planned improvement. In the meantime:
- Always verify checksums before installing
- Prefer building from source when possible
- Review the C# source (`shim/PowerShellShim.cs`) — it's a single file

## Security Considerations

- In session proxy mode, the shim spawns real `powershell.exe` as a child process and forwards stdin. Only WSL commands with specific problematic tokens are rewritten; all other input passes through unchanged.
- The profile wraps native tools by merging stderr to stdout as plain strings. This does not suppress actual errors — exit codes are preserved.
- The shim classifier is conservative: unknown commands default to PowerShell passthrough (not WSL routing).

## Reporting Vulnerabilities

If you find a security issue, please email the maintainer directly rather than opening a public issue.

Contact: Open a private issue on the repository or reach out via GitHub profile.

## Supported Versions

| Version | Supported |
|---|---|
| 1.6.x | Yes (current) |
| 1.5.x | Yes (session proxy, one-shot, profile) |
| 1.3.x–1.4.x | Partial — one-shot mode only, no session proxy |
| ≤ 1.2.x | No |
