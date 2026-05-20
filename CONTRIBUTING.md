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
├── shim/                    # C# shim source
│   ├── PowerShellShim.cs    # Main logic
│   └── PowerShellShim.csproj
├── profile/                 # PowerShell profile
│   └── Microsoft.PowerShell_profile.ps1
├── .github/workflows/       # CI
│   └── ci.yml
├── install.ps1              # Installer
├── test.ps1                 # Test suite
├── README.md
├── CONTRIBUTING.md
├── CHANGELOG.md
└── LICENSE
```

## Making Changes

1. **Fork** the repo
2. **Create a branch** (`git checkout -b fix/my-fix`)
3. **Make your changes** — follow existing code style
4. **Run tests** (`.\test.ps1`) — all must pass
5. **Commit** with a clear message
6. **Open a PR** with a description of what and why

## What to Contribute

- **New bash commands** — add to the `$wslCommands` array in the profile and `bashCommands` array in the shim
- **Edge cases** — if you find a command that breaks, add a test and fix it
- **Distro support** — test with other WSL distributions
- **Documentation** — improve the README, add examples

## Code Style

- **C#**: follow existing patterns, no unnecessary abstractions
- **PowerShell**: avoid aliases in scripts, use full cmdlet names
- **Comments**: explain *why*, not *what*

## Reporting Issues

Include:
- The command that failed
- The error output
- Your WSL distro (`wsl --list`)
- Whether you're using the shim, profile, or both
- Debug output (`$env:PWSH_SHIM_DEBUG = "1"`)
