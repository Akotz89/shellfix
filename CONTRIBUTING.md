# Contributing to shellfix

Thanks for your interest. shellfix shadows `powershell.exe`, so changes need clear scope and evidence.

## Quick Start

```powershell
git clone https://github.com/Akotz89/shellfix.git
cd shellfix
.\install.ps1
.\test-ci-smoke.ps1
.\test.ps1
```

## Development

### Building the shim

```powershell
cd shim
dotnet publish -c Release -o out
```

### Running tests

```powershell
.\test-ci-smoke.ps1 -ShimPath .\shim\out\powershell.exe
.\test.ps1 -ShimPath .\shim\out\powershell.exe
.\test-proxy.ps1 -ShimPath .\shim\out\powershell.exe
.\test-replay.ps1 -ShimPath .\shim\out\powershell.exe
.\install.ps1 -TestShortcuts
```

### Project structure

```
shellfix/
├── shim/                    # Layer 1: C# shim source
│   ├── PowerShellShim.cs    #   Classifier, router, proxy, escaping
│   └── PowerShellShim.csproj
├── profile/                 # Layers 2+3: PowerShell profile
│   └── Microsoft.PowerShell_profile.ps1
│       # Layer 2: 50+ bash wrappers
│       # Layer 3: NativeCommandError suppression
├── .github/workflows/       # CI + Release automation
│   ├── ci.yml               #   Build on push/PR
│   └── release.yml          #   Build + GitHub Release on tag push
├── install.ps1              # Installer (build, profile, IDE shortcuts)
├── launch-ide.bat           # Generic IDE launcher (PATH prepend)
├── test-ci-smoke.ps1        # CI smoke suite (5 checks)
├── test.ps1                 # Full one-shot/profile suite (48 tests)
├── test-proxy.ps1           # Session proxy test suite (16 tests)
├── test-replay.ps1          # Historical session replay (10 tests)
├── README.md
├── CHANGELOG.md
├── CONTRIBUTING.md
├── SECURITY.md
└── LICENSE
```

## Making Changes

1. **Fork** the repo
2. **Create a branch** (`git checkout -b fix/my-fix`)
3. **Make changes** — follow existing code style
4. **Run relevant tests** — at minimum `.\test-ci-smoke.ps1`
5. **Commit** with a clear message
6. **Open a PR**

Use the PR template. Include the exact verification commands, CI run, linked GitHub issue, linked Linear issue when present, risk, and install/release impact.

## What to Contribute

### Class 1 (Bash routing)
- Add commands to `bashCommands` in the shim and `$wslCommands` in the profile
- Fix edge cases in path translation or quoting

### Class 2 (PS quoting)
- Improve `HasDangerousQuoting()` heuristic with new patterns
- Report commands that should trigger `-File` mode but don't

### Class 3 (NativeCommandError)
- Add tools to `$nativeTools` in the profile that write to stderr
- Test exit code propagation for wrapped tools

### General
- Test with other WSL distributions
- Improve documentation and examples
- Report failure patterns from any IDE agent

## Documentation And Release Claims

- Do not say behavior is fixed without a passing test, manual verification command, CI run, commit, PR, tag, or release URL.
- Keep public wording operator-facing. Avoid chat-derived phrasing, dramatic claims, and unverified implementation stories.
- Put broad claim evidence in `docs/CLAIM_EVIDENCE.md`.
- Use `docs/MAINTAINER_CHECKLIST.md` before closing issues or publishing releases.
- Keep `docs/TRACKING.md` aligned when GitHub/Linear/release status changes.

## Code Style

- **C#**: follow existing patterns, no unnecessary abstractions
- **PowerShell**: avoid aliases in scripts, use full cmdlet names
- **Comments**: explain *why*, not *what*

## Reporting Issues

Include:
- The command that failed
- The error output (full text)
- Which class of failure (1/2/3)
- Your WSL distro (`wsl --list --verbose`)
- Your configured shellfix distro (`$env:SHELLFIX_WSL_DISTRO`)
- The shim path (`(Get-Command powershell.exe).Source`)
- The shim hash (`Get-FileHash <path> -Algorithm SHA256`)
- Debug output (`$env:PWSH_SHIM_DEBUG = "1"`)
- Whether the profile is loaded (`$env:PS_PROFILE_LOADED`)
