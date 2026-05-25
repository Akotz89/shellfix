# shellfix compatibility bootstrapper
# Preferred interface: shellfix.exe install|doctor|status|uninstall

param(
    [string]$WslDistro = "Ubuntu-24.04",
    [string]$BinDir = "$env:USERPROFILE\bin",
    [switch]$SkipBuild,
    [switch]$SkipProfile,
    [switch]$SkipShortcuts,
    [switch]$SkipAntigravitySettings,
    [switch]$TestShortcuts,
    [switch]$TestAntigravitySettings,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

function Write-Info { param([string]$Message) Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Write-Ok { param([string]$Message) Write-Host "  [OK] $Message" -ForegroundColor Green }
function Write-ErrorLine { param([string]$Message) Write-Host "  [ERROR] $Message" -ForegroundColor Red }

function Assert-PowerShellVersion {
    if ($PSVersionTable.PSVersion.Major -lt 5) {
        throw "PowerShell 5.1 or newer is required."
    }
}

function Assert-Dotnet {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw ".NET 8 SDK is required to build from source. Install it or rerun with -SkipBuild using release assets."
    }
}

function Find-ShellfixCli {
    param([string]$Root)

    $candidates = @(
        (Join-Path $Root "shellfix.exe"),
        (Join-Path $Root "src\Shellfix.Cli\out\shellfix.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Shellfix\shellfix.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }

    return $null
}

function Publish-FromSource {
    param([string]$Root)

    Assert-Dotnet

    $shimProject = Join-Path $Root "shim\PowerShellShim.csproj"
    $cliProject = Join-Path $Root "src\Shellfix.Cli\Shellfix.Cli.csproj"
    $shimOut = Join-Path $Root "shim\out"
    $cliOut = Join-Path $Root "src\Shellfix.Cli\out"

    if (-not (Test-Path $shimProject)) { throw "Cannot find shim project: $shimProject" }
    if (-not (Test-Path $cliProject)) { throw "Cannot find CLI project: $cliProject" }

    Write-Info "Building shellfix shim"
    dotnet publish $shimProject -c Release -o $shimOut --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Shim build failed." }
    Write-Ok "Shim built: $(Join-Path $shimOut "powershell.exe")"

    Write-Info "Building shellfix CLI"
    dotnet publish $cliProject `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $cliOut `
        --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "CLI build failed." }
    Write-Ok "CLI built: $(Join-Path $cliOut "shellfix.exe")"

    return Join-Path $cliOut "shellfix.exe"
}

Assert-PowerShellVersion
$root = $PSScriptRoot

Write-Host ""
Write-Host "shellfix install.ps1 is now a compatibility bootstrapper." -ForegroundColor Yellow
Write-Host "Preferred interface after install: shellfix doctor, shellfix status, shellfix uninstall." -ForegroundColor Yellow
Write-Host ""

try {
    if ($SkipBuild) {
        $cli = Find-ShellfixCli -Root $root
        if (-not $cli) {
            throw "Cannot find shellfix.exe. Place release assets next to install.ps1 or rerun without -SkipBuild."
        }
    } else {
        $cli = Publish-FromSource -Root $root
    }

    if ($TestAntigravitySettings) {
        & $cli test --antigravity-settings
        exit $LASTEXITCODE
    }

    if ($TestShortcuts) {
        & $cli test --shortcuts
        exit $LASTEXITCODE
    }

    if ($Uninstall) {
        & $cli uninstall
        exit $LASTEXITCODE
    }

    $argsList = @(
        "install",
        "--source-root", $root,
        "--wsl-distro", $WslDistro,
        "--bin-dir", $BinDir
    )

    if ($SkipBuild) { $argsList += "--skip-build" }
    if ($SkipProfile) { $argsList += "--skip-profile" }
    if ($SkipShortcuts) { $argsList += "--skip-shortcuts" }
    if ($SkipAntigravitySettings) { $argsList += "--skip-antigravity-settings" }

    & $cli @argsList
    exit $LASTEXITCODE
} catch {
    Write-ErrorLine $_.Exception.Message
    exit 1
}
