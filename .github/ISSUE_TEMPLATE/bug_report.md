---
name: Bug Report
about: A command that shellfix should handle but doesn't
title: ''
labels: bug
assignees: ''
---

## Which failure class?

- [ ] Class 1: Bash command mangled by PowerShell
- [ ] Class 2: Complex quoting breaks PS parser
- [ ] Class 3: NativeCommandError (stderr red text)
- [ ] Not sure

## The command that failed

```
paste the exact command here
```

## Expected behavior

What should have happened.

## Actual behavior

What actually happened. Include the full error output.

## Environment

- **Windows version:** (e.g., Windows 11 23H2)
- **WSL distro:** (output of `wsl --list --verbose`)
- **Configured shellfix distro:** (output of `$env:SHELLFIX_WSL_DISTRO`)
- **PowerShell version:** (output of `$PSVersionTable.PSVersion`)
- **IDE/Agent:** (e.g., Cursor 0.48, Windsurf, Antigravity)
- **shellfix version:** (release tag or `git log -1 --oneline`)
- **Install path:** (output of `(Get-Command powershell.exe).Source`)
- **Shim hash:** (output of `Get-FileHash <path-to-shellfix-powershell.exe> -Algorithm SHA256`)
- **Profile loaded:** (output of `$env:PS_PROFILE_LOADED`)

## Runtime mode

- [ ] One-shot: `powershell -Command`
- [ ] Session proxy / interactive terminal
- [ ] Agent `run_command`
- [ ] Not sure

## Debug output

Set `$env:PWSH_SHIM_DEBUG = "1"` and re-run the command, then paste the `[SHIM]` lines:

```
paste debug output here
```

## Verification attempted

```powershell
.\test-ci-smoke.ps1 -ShimPath <path-to-shellfix-powershell.exe>
```
