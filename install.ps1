# wsl-shell-hardening — Install Script
# Run from the repo root: .\install.ps1

param(
    [string]$WslDistro = "Ubuntu-24.04",
    [string]$BinDir = "$env:USERPROFILE\bin",
    [switch]$SkipBuild,
    [switch]$SkipProfile,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

function Write-Step { param($msg) Write-Host "`n[$([char]0x2192)] $msg" -ForegroundColor Cyan }
function Write-Ok { param($msg) Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Warn { param($msg) Write-Host "  [!!] $msg" -ForegroundColor Yellow }
function Write-Err { param($msg) Write-Host "  [X] $msg" -ForegroundColor Red }

# ================================================================
# Uninstall
# ================================================================
if ($Uninstall) {
    Write-Step "Uninstalling wsl-shell-hardening"
    
    $shimPath = Join-Path $BinDir "powershell.exe"
    if (Test-Path $shimPath) {
        Remove-Item $shimPath -Force
        Write-Ok "Removed shim: $shimPath"
    } else {
        Write-Warn "Shim not found: $shimPath"
    }
    
    $profilePath = "$env:USERPROFILE\Documents\WindowsPowerShell\Microsoft.PowerShell_profile.ps1"
    if (Test-Path $profilePath) {
        $content = Get-Content $profilePath -Raw
        if ($content -match 'Antigravity Agent Shell Hardening') {
            Write-Warn "Profile contains shell hardening. Remove manually: $profilePath"
        }
    }
    
    Write-Ok "Uninstall complete. Restart your IDE."
    exit 0
}

# ================================================================
# Pre-flight checks
# ================================================================
Write-Step "Pre-flight checks"

# Check WSL
try {
    $wslCheck = wsl.exe -d $WslDistro -e echo ok 2>&1
    if ($wslCheck -match 'ok') {
        Write-Ok "WSL distribution '$WslDistro' is available"
    } else {
        Write-Err "WSL distribution '$WslDistro' not found"
        Write-Host "  Available distributions:"
        wsl.exe --list --quiet | ForEach-Object { Write-Host "    - $_" }
        exit 1
    }
} catch {
    Write-Err "WSL is not installed or not running"
    Write-Host "  Install WSL: wsl --install"
    exit 1
}

# Check .NET SDK
if (-not $SkipBuild) {
    try {
        $dotnetVer = dotnet --version 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Ok ".NET SDK: $dotnetVer"
        } else {
            throw "not found"
        }
    } catch {
        Write-Err ".NET 8 SDK is required to build the shim"
        Write-Host "  Install: https://dotnet.microsoft.com/download/dotnet/8.0"
        exit 1
    }
}

# ================================================================
# Build the shim
# ================================================================
if (-not $SkipBuild) {
    Write-Step "Building shim"
    
    $shimDir = Join-Path $PSScriptRoot "shim"
    $outDir = Join-Path $shimDir "out"
    
    # Update distro name if not default
    if ($WslDistro -ne "Ubuntu-24.04") {
        $csFile = Join-Path $shimDir "PowerShellShim.cs"
        $content = Get-Content $csFile -Raw
        $content = $content -replace 'const string WslDistro = "Ubuntu-24.04"', "const string WslDistro = `"$WslDistro`""
        [System.IO.File]::WriteAllText($csFile, $content, [System.Text.UTF8Encoding]::new($false))
        Write-Ok "Updated distro to: $WslDistro"
    }
    
    dotnet publish $shimDir -c Release -o $outDir --nologo 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Err "Build failed"
        exit 1
    }
    Write-Ok "Shim compiled"
}

# ================================================================
# Install shim
# ================================================================
Write-Step "Installing shim to $BinDir"

# Create bin directory
if (-not (Test-Path $BinDir)) {
    New-Item -Path $BinDir -ItemType Directory -Force | Out-Null
    Write-Ok "Created: $BinDir"
}

# Copy shim
$outExe = Join-Path $PSScriptRoot "shim\out\powershell.exe"
$targetExe = Join-Path $BinDir "powershell.exe"
Copy-Item $outExe $targetExe -Force
Write-Ok "Installed: $targetExe"

# Ensure bin is in PATH (before System32)
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($userPath -notmatch [regex]::Escape($BinDir)) {
    [Environment]::SetEnvironmentVariable('Path', "$BinDir;$userPath", 'User')
    $env:Path = "$BinDir;$env:Path"
    Write-Ok "Added $BinDir to user PATH"
} else {
    Write-Ok "$BinDir already in PATH"
}

# Verify it's first
$resolved = (Get-Command powershell.exe -ErrorAction SilentlyContinue).Source
if ($resolved -eq $targetExe) {
    Write-Ok "Shim is first in PATH"
} else {
    Write-Warn "Shim may not be first in PATH. Found: $resolved"
    Write-Warn "You may need to restart your terminal/IDE"
}

# ================================================================
# Install profile
# ================================================================
if (-not $SkipProfile) {
    Write-Step "Installing PowerShell profile"
    
    $profileDir = "$env:USERPROFILE\Documents\WindowsPowerShell"
    $profilePath = Join-Path $profileDir "Microsoft.PowerShell_profile.ps1"
    $sourceProfile = Join-Path $PSScriptRoot "profile\Microsoft.PowerShell_profile.ps1"
    
    # Update distro name in profile if not default
    if ($WslDistro -ne "Ubuntu-24.04") {
        $content = Get-Content $sourceProfile -Raw
        $content = $content -replace 'Ubuntu-24.04', $WslDistro
        if (-not (Test-Path $profileDir)) {
            New-Item -Path $profileDir -ItemType Directory -Force | Out-Null
        }
        [System.IO.File]::WriteAllText($profilePath, $content, [System.Text.UTF8Encoding]::new($false))
    } else {
        if (-not (Test-Path $profileDir)) {
            New-Item -Path $profileDir -ItemType Directory -Force | Out-Null
        }
        Copy-Item $sourceProfile $profilePath -Force
    }
    Write-Ok "Profile installed: $profilePath"
}

# ================================================================
# Done
# ================================================================
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Installation complete!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Restart your IDE for changes to take effect."
Write-Host ""
Write-Host "  Controls:"
Write-Host "    Disable shim:  `$env:PWSH_SHIM_BYPASS = '1'"
Write-Host "    Debug mode:    `$env:PWSH_SHIM_DEBUG = '1'"
Write-Host "    Uninstall:     .\install.ps1 -Uninstall"
Write-Host ""
Write-Host "  Verify:          .\test.ps1"
Write-Host ""
