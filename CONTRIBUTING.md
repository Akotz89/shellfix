# Contributing to shellfix

Thanks for your interest! Here's how to help.

## Quick Start

```powershell
git clone https://github.com/Akotz89/shellfix.git
cd shellfix
.\install.ps1
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
.\test.ps1 -Verbose
```

### Project structure

```
shellfix/
├── shim/                    # Layer 1: C# shim source
│   ├── PowerShellShim.cs    #   Classifier, router, escaping
│   └── PowerShellShim.csproj
├── profile/                 # Layers 2+3: PowerShell profile
│   └── Microsoft.PowerShell_profile.ps1
│       # Layer 2: 50+ bash wrappers
│       # Layer 3: NativeCommandError suppression
├── .github/workflows/       # CI
│   └── ci.yml
├── install.ps1              # Installer with pre-flight checks
├── test.ps1                 # Three-class test suite
├── README.md
├── CONTRIBUTING.md
├── CHANGELOG.md
└── LICENSE
```

## Making Changes

1. **Fork** the repo
2. **Create a branch** (`git checkout -b fix/my-fix`)
3. **Make changes** — follow existing code style
4. **Run tests** (`.\test.ps1`) — all must pass
5. **Commit** with a clear message
6. **Open a PR**

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

## Code Style

- **C#**: follow existing patterns, no unnecessary abstractions
- **PowerShell**: avoid aliases in scripts, use full cmdlet names
- **Comments**: explain *why*, not *what*

## Reporting Issues

Include:
- The command that failed
- The error output (full text)
- Which class of failure (1/2/3)
- Your WSL distro (`wsl --list`)
- Debug output (`$env:PWSH_SHIM_DEBUG = "1"`)
- Whether the profile is loaded (`$env:PS_PROFILE_LOADED`)
