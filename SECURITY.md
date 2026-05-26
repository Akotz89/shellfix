# Security Policy

## Trust Model

shellfix intentionally shadows `powershell.exe` in your IDE's PATH. This is a sensitive operation: IDE child processes that resolve bare `powershell` will reach the shim before the system PowerShell binary.

### What shellfix does

- Classifies incoming commands as bash or PowerShell
- Routes explicit WSL commands directly through `wsl.exe` and passes PowerShell commands to the configured PowerShell backend
- In session proxy mode, spawns the backend PowerShell and intercepts known fragile command shapes before PowerShell parses them, including WSL heredoc stdin, WSL/bash multiline Python, and native inline Python/Node payloads
- Writes temporary `.ps1` scripts to `%TEMP%` for complex PS commands (deleted immediately after execution)
- Writes temporary `.py` or `.js` files to `%TEMP%` for native inline interpreter payloads (deleted immediately after execution)
- Installs through `shellfix.exe`, which records reversible install state in `%LOCALAPPDATA%\Shellfix\state.json`
- **Does not** make network requests, store credentials, or access files beyond installation targets and what the intercepted command accesses

### What shellfix does NOT do

- It does not modify, log, or exfiltrate your commands or output
- The shim does not persist data between invocations; the management CLI persists install state and backups for rollback
- It does not run with elevated privileges (it inherits the IDE's permissions)

### PATH Shadowing Risk

The shim works by placing a `powershell.exe` binary earlier in PATH than `C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe`. This means:

- **Every** invocation of `powershell` or `powershell.exe` from your IDE will hit the shim first
- The `PWSH_SHIM_BYPASS=1` environment variable is the kill switch — set it to skip the shim entirely
- The installer records backups under `%LOCALAPPDATA%\Shellfix\backups\` and state in `%LOCALAPPDATA%\Shellfix\state.json`
- Run `shellfix doctor` to audit current routing, profile, shortcuts, WSL, and Antigravity settings
- Run `shellfix uninstall` to restore recorded profile, shortcut, settings, and PATH changes

## Verifying Release Binaries

Every GitHub Release includes a `checksums.txt` file with SHA256 hashes for all assets.

### Verification steps (PowerShell)

```powershell
# 1. Download the release assets
# 2. Verify the checksum matches
$expectedShim = Get-Content checksums.txt | Where-Object { $_ -match 'powershell.exe' } | ForEach-Object { $_.Split(' ')[0] }
$actualShim = (Get-FileHash powershell.exe -Algorithm SHA256).Hash.ToLower()
$expectedCli = Get-Content checksums.txt | Where-Object { $_ -match 'shellfix.exe' } | ForEach-Object { $_.Split(' ')[0] }
$actualCli = (Get-FileHash shellfix.exe -Algorithm SHA256).Hash.ToLower()
if ($expectedShim -eq $actualShim -and $expectedCli -eq $actualCli) { Write-Host "Checksums match" -ForegroundColor Green }
else { Write-Host "CHECKSUM MISMATCH - do not use these binaries" -ForegroundColor Red }
```

### Building from source

To avoid trusting a pre-built binary, build from source:

```powershell
git clone https://github.com/Akotz89/shellfix.git
cd shellfix
dotnet publish shim/PowerShellShim.csproj -c Release -o shim/out --nologo
dotnet publish src/Shellfix.Cli/Shellfix.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o src/Shellfix.Cli/out --nologo
# Verify: compare Get-FileHash shim/out/powershell.exe with your own build
```

### Code Signing

Release binaries are **not** currently code-signed. This is a planned improvement. In the meantime:
- Always verify checksums before installing
- Prefer building from source when possible
- Review the C# source in `shim/`, `src/Shellfix.Core/`, and `src/Shellfix.Cli/`

## Security Considerations

- In session proxy mode, the shim spawns the backend PowerShell as a child process and forwards ordinary stdin. Known fragile agent command shapes are executed directly by the shim so PowerShell does not parse foreign-language payloads.
- The profile wraps native tools by merging stderr to stdout as plain strings. This does not suppress actual errors — exit codes are preserved.
- The shim classifier is conservative: unsupported heredoc syntax is rejected before PowerShell parses it, and unknown commands default to PowerShell file-mode execution rather than WSL routing.
- Antigravity settings repair writes only the relevant terminal profile keys, enables ConPTY for the compiled shim terminal profile, and stores a backup before changing the file. `shellfix doctor` also reports live Antigravity PowerShell child processes that bypass the installed shim and stale ConPTY-disabled terminal mode.

## Reporting Vulnerabilities

If you find a security issue, please email the maintainer directly rather than opening a public issue.

Contact: Open a private issue on the repository or reach out via GitHub profile.

## Supported Versions

| Version | Supported |
|---|---|
| Current `master` / latest release | Yes |
| Older pre-CLI releases | Best effort |
