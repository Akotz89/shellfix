# shellfix - Install Script
# Run from the repo root: .\install.ps1

param(
    [string]$WslDistro = "Ubuntu-24.04",
    [string]$BinDir = "$env:USERPROFILE\bin",
    [switch]$SkipBuild,
    [switch]$SkipProfile,
    [switch]$SkipShortcuts,
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
    Write-Step "Uninstalling shellfix"

    $profileDir = "$env:USERPROFILE\Documents\WindowsPowerShell"
    $profilePath = Join-Path $profileDir "Microsoft.PowerShell_profile.ps1"
    $snippetPath = Join-Path $profileDir "shellfix_profile.ps1"
    $beginMarker = "# >>> shellfix >>>"
    $endMarker   = "# <<< shellfix <<<"
    $enc = [System.Text.UTF8Encoding]::new($false)

    # Remove guarded block from user profile
    if (Test-Path $profilePath) {
        $content = [System.IO.File]::ReadAllText($profilePath, $enc)
        if ($content.Contains($beginMarker)) {
            $pattern = "(?s)\r?\n?$([regex]::Escape($beginMarker)).*?$([regex]::Escape($endMarker))\r?\n?"
            $content = [regex]::Replace($content, $pattern, "`n")
            $content = $content.Trim() + "`n"
            [System.IO.File]::WriteAllText($profilePath, $content, $enc)
            Write-Ok "Removed shellfix block from profile: $profilePath"
        } else {
            Write-Warn "No shellfix block found in profile (already clean)"
        }
    }

    # Remove snippet file
    if (Test-Path $snippetPath) {
        Remove-Item $snippetPath -Force
        Write-Ok "Removed snippet: $snippetPath"
    }

    # Remove shim binary
    $shimExe = Join-Path $BinDir "powershell.exe"
    $shimPdb = Join-Path $BinDir "powershell.pdb"
    if (Test-Path $shimExe) {
        try {
            Remove-Item $shimExe -Force
            Write-Ok "Removed shim: $shimExe"
        } catch {
            Write-Warn "Could not remove shim (in use?): $shimExe"
            Write-Warn "Close your IDE and try again, or delete manually."
        }
    }
    if (Test-Path $shimPdb) {
        Remove-Item $shimPdb -Force -ErrorAction SilentlyContinue
    }

    # Restore shortcut backups
    $backupDir = Join-Path $env:USERPROFILE ".shellfix-backup"
    if (Test-Path $backupDir) {
        $backups = Get-ChildItem $backupDir -Filter "*.lnk" -ErrorAction SilentlyContinue
        foreach ($backup in $backups) {
            # Try to find the original shortcut location
            $desktopPath = [Environment]::GetFolderPath("Desktop")
            $startMenuPath = [Environment]::GetFolderPath("Programs")
            $possibleDests = @(
                (Join-Path $desktopPath $backup.Name),
                (Join-Path $startMenuPath $backup.Name)
            )
            foreach ($dest in $possibleDests) {
                if (Test-Path $dest) {
                    Copy-Item $backup.FullName $dest -Force
                    Write-Ok "Restored shortcut backup: $($backup.Name)"
                    break
                }
            }
        }
        Write-Ok "Shortcut backups preserved in: $backupDir"
    }

    Write-Host ""
    Write-Host "  shellfix uninstalled. Restart your IDE to take effect." -ForegroundColor Green
    exit 0
}

# ================================================================
# IDE Definitions — add new IDEs here
# ================================================================
$KnownIDEs = @(
    @{
        Name     = "VS Code"
        ExePaths = @(
            "$env:LOCALAPPDATA\Programs\Microsoft VS Code\Code.exe"
        )
        ShortcutNames = @("Visual Studio Code.lnk", "Code.lnk")
    },
    @{
        Name     = "VS Code Insiders"
        ExePaths = @(
            "$env:LOCALAPPDATA\Programs\Microsoft VS Code Insiders\Code - Insiders.exe"
        )
        ShortcutNames = @("Visual Studio Code - Insiders.lnk", "Code - Insiders.lnk")
    },
    @{
        Name     = "Cursor"
        ExePaths = @(
            "$env:LOCALAPPDATA\Programs\cursor\Cursor.exe",
            "$env:LOCALAPPDATA\cursor\Cursor.exe"
        )
        ShortcutNames = @("Cursor.lnk")
    },
    @{
        Name     = "Windsurf"
        ExePaths = @(
            "$env:LOCALAPPDATA\Programs\Windsurf\Windsurf.exe"
        )
        ShortcutNames = @("Windsurf.lnk")
    },
    @{
        Name     = "Antigravity IDE"
        ExePaths = @(
            "$env:LOCALAPPDATA\Programs\Antigravity IDE\Antigravity IDE.exe"
        )
        ShortcutNames = @("Antigravity IDE.lnk", "Antigravity.lnk")
    }
)

# ================================================================
# Helpers
# ================================================================
function Find-IDEInstalls {
    <#
    .SYNOPSIS
        Discovers installed VS Code-based IDEs and their shortcuts.
    #>
    $found = @()
    $shell = New-Object -ComObject WScript.Shell

    foreach ($ide in $KnownIDEs) {
        $exePath = $null
        foreach ($p in $ide.ExePaths) {
            if (Test-Path $p) { $exePath = $p; break }
        }
        if (-not $exePath) { continue }

        # Search for shortcuts in common locations
        $shortcuts = @()
        $searchDirs = @(
            "$env:USERPROFILE\Desktop",
            "$env:PUBLIC\Desktop",
            "$env:APPDATA\Microsoft\Windows\Start Menu\Programs"
        )
        foreach ($dir in $searchDirs) {
            if (-not (Test-Path $dir)) { continue }
            foreach ($name in $ide.ShortcutNames) {
                $lnkFiles = Get-ChildItem $dir -Filter $name -Recurse -ErrorAction SilentlyContinue
                foreach ($lnk in $lnkFiles) {
                    $shortcuts += $lnk.FullName
                }
            }
        }

        $found += @{
            Name      = $ide.Name
            ExePath   = $exePath
            Shortcuts = $shortcuts
        }
    }
    return $found
}

function Patch-Shortcut {
    <#
    .SYNOPSIS
        Modifies a .lnk shortcut to prepend the shim directory to PATH
        before launching the IDE. Creates a .bak backup first.
    #>
    param(
        [string]$LnkPath,
        [string]$BinDir,
        [string]$ExePath
    )

    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($LnkPath)

    # Skip if already patched (target is cmd.exe)
    if ($lnk.TargetPath -match 'cmd\.exe$') {
        # Check if it's our patch
        if ($lnk.Arguments -match [regex]::Escape($BinDir)) {
            Write-Ok "Already patched: $(Split-Path $LnkPath -Leaf)"
            return
        }
    }

    # Backup original
    $backupPath = "$LnkPath.shellfix-backup"
    if (-not (Test-Path $backupPath)) {
        Copy-Item $LnkPath $backupPath -Force
    }

    # Preserve existing arguments
    $origArgs = $lnk.Arguments

    # Patch: use cmd.exe to prepend PATH then launch
    $lnk.TargetPath = "C:\Windows\System32\cmd.exe"
    $lnk.Arguments = "/C set ""PATH=$BinDir;%PATH%"" && start """" ""$ExePath"" $origArgs"
    $lnk.WorkingDirectory = Split-Path $ExePath -Parent
    $lnk.WindowStyle = 7  # Minimized (hides cmd flash)
    $lnk.Save()

    Write-Ok "Patched: $(Split-Path $LnkPath -Leaf)"
}

function Restore-Shortcut {
    <#
    .SYNOPSIS
        Restores a shortcut from its .shellfix-backup file.
    #>
    param([string]$LnkPath)

    $backupPath = "$LnkPath.shellfix-backup"
    if (Test-Path $backupPath) {
        Copy-Item $backupPath $LnkPath -Force
        Remove-Item $backupPath -Force
        Write-Ok "Restored: $(Split-Path $LnkPath -Leaf)"
    } else {
        Write-Warn "No backup found for: $(Split-Path $LnkPath -Leaf)"
    }
}

# ================================================================
# Uninstall
# ================================================================
if ($Uninstall) {
    Write-Step "Uninstalling shellfix"

    $shimPath = Join-Path $BinDir "powershell.exe"
    if (Test-Path $shimPath) {
        Remove-Item $shimPath -Force
        Write-Ok "Removed shim: $shimPath"
    } else {
        Write-Warn "Shim not found: $shimPath"
    }

    # Remove PDB if present
    $pdbPath = Join-Path $BinDir "powershell.pdb"
    if (Test-Path $pdbPath) { Remove-Item $pdbPath -Force }

    # Restore all IDE shortcuts
    Write-Step "Restoring IDE shortcuts"
    $ides = Find-IDEInstalls
    if ($ides.Count -eq 0) {
        Write-Warn "No IDE installations found"
    }
    foreach ($ide in $ides) {
        foreach ($lnk in $ide.Shortcuts) {
            Restore-Shortcut -LnkPath $lnk
        }
    }

    $profilePath = "$env:USERPROFILE\Documents\WindowsPowerShell\Microsoft.PowerShell_profile.ps1"
    if (Test-Path $profilePath) {
        $content = Get-Content $profilePath -Raw
        if ($content -match 'shellfix') {
            Write-Warn "Profile contains shellfix entries. Remove manually: $profilePath"
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

# Check ExecutionPolicy
$policy = Get-ExecutionPolicy -Scope CurrentUser
if ($policy -eq 'Restricted' -or $policy -eq 'Undefined') {
    Write-Warn "ExecutionPolicy is '$policy' - agent scripts will be blocked"
    Write-Step "Setting ExecutionPolicy to RemoteSigned for CurrentUser"
    try {
        Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser -Force
        Write-Ok "ExecutionPolicy set to RemoteSigned"
    } catch {
        Write-Warn "Could not set ExecutionPolicy. You may need to run:"
        Write-Host "  Set-ExecutionPolicy RemoteSigned -Scope CurrentUser"
    }
} else {
    Write-Ok "ExecutionPolicy: $policy"
}

# Check LongPathsEnabled (260-char MAX_PATH limit)
$regPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem'
$longPaths = Get-ItemProperty -Path $regPath -Name 'LongPathsEnabled' -ErrorAction SilentlyContinue
if ($longPaths -and $longPaths.LongPathsEnabled -eq 1) {
    Write-Ok "Long paths enabled (MAX_PATH bypass)"
} else {
    Write-Warn "Long paths NOT enabled - deep node_modules will fail"
    Write-Step "Enabling LongPathsEnabled (requires admin)"
    try {
        Set-ItemProperty -Path $regPath -Name 'LongPathsEnabled' -Value 1 -Type DWord -Force
        Write-Ok "LongPathsEnabled set to 1 (reboot may be required)"
    } catch {
        Write-Warn "Could not set LongPathsEnabled (need admin). Run as Administrator:"
        Write-Host "  Set-ItemProperty -Path '$regPath' -Name 'LongPathsEnabled' -Value 1 -Type DWord"
    }
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

# Copy shim — check two possible locations:
#   1. shim/out/powershell.exe (built from source)
#   2. powershell.exe in same directory as install.ps1 (downloaded from release)
$outExe = Join-Path $PSScriptRoot "shim\out\powershell.exe"
if (-not (Test-Path $outExe)) {
    $outExe = Join-Path $PSScriptRoot "powershell.exe"
}
if (-not (Test-Path $outExe)) {
    Write-Err "Cannot find shim binary. Either build from source (remove -SkipBuild) or place powershell.exe next to install.ps1"
    exit 1
}
$targetExe = Join-Path $BinDir "powershell.exe"
Copy-Item $outExe $targetExe -Force
Write-Ok "Installed: $targetExe (from $outExe)"

# Copy PDB for debugging
$outPdb = Join-Path $PSScriptRoot "shim\out\powershell.pdb"
if (Test-Path $outPdb) {
    Copy-Item $outPdb (Join-Path $BinDir "powershell.pdb") -Force
}

# Ensure bin is in user PATH
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($userPath -notmatch [regex]::Escape($BinDir)) {
    [Environment]::SetEnvironmentVariable('Path', "$BinDir;$userPath", 'User')
    $env:Path = "$BinDir;$env:Path"
    Write-Ok "Added $BinDir to user PATH"
} else {
    Write-Ok "$BinDir already in PATH"
}

# ================================================================
# Install profile (non-destructive — preserves existing user profile)
# ================================================================
if (-not $SkipProfile) {
    Write-Step "Installing PowerShell profile"

    $profileDir = "$env:USERPROFILE\Documents\WindowsPowerShell"
    $profilePath = Join-Path $profileDir "Microsoft.PowerShell_profile.ps1"
    $snippetPath = Join-Path $profileDir "shellfix_profile.ps1"

    # --- Sentinel markers for the guarded block ---
    $beginMarker = "# >>> shellfix >>>"
    $endMarker   = "# <<< shellfix <<<"

    # --- Locate the shellfix profile source ---
    $sourceProfile = Join-Path $PSScriptRoot "profile\Microsoft.PowerShell_profile.ps1"
    if (-not (Test-Path $sourceProfile)) {
        $sourceProfile = Join-Path $PSScriptRoot "Microsoft.PowerShell_profile.ps1"
    }
    if (-not (Test-Path $sourceProfile)) {
        Write-Warn "Profile source not found. Skipping profile installation."
        Write-Warn "Place Microsoft.PowerShell_profile.ps1 next to install.ps1 or clone the full repo."
    } else {
        # Ensure profile directory exists
        if (-not (Test-Path $profileDir)) {
            New-Item -Path $profileDir -ItemType Directory -Force | Out-Null
        }

        # --- Install shellfix snippet file ---
        $snippetContent = Get-Content $sourceProfile -Raw
        if ($WslDistro -ne "Ubuntu-24.04") {
            $snippetContent = $snippetContent -replace 'Ubuntu-24.04', $WslDistro
        }
        $enc = [System.Text.UTF8Encoding]::new($false)
        [System.IO.File]::WriteAllText($snippetPath, $snippetContent, $enc)
        Write-Ok "Snippet installed: $snippetPath"

        # --- Inject guarded dot-source block into user profile ---
        $dotSourceBlock = @"

$beginMarker
# shellfix — do not edit this block manually. Managed by install.ps1.
# To remove: run .\install.ps1 -Uninstall, or delete this block and $snippetPath
if (Test-Path '$snippetPath') { . '$snippetPath' }
$endMarker
"@

        if (Test-Path $profilePath) {
            $existingProfile = [System.IO.File]::ReadAllText($profilePath, $enc)
            if ($existingProfile.Contains($beginMarker)) {
                # Already present — replace in place (idempotent reinstall)
                $pattern = "(?s)$([regex]::Escape($beginMarker)).*?$([regex]::Escape($endMarker))"
                $existingProfile = [regex]::Replace($existingProfile, $pattern, $dotSourceBlock.TrimStart())
                [System.IO.File]::WriteAllText($profilePath, $existingProfile, $enc)
                Write-Ok "Profile updated (idempotent reinstall): $profilePath"
            } else {
                # Append — preserve existing user content
                $existingProfile = $existingProfile.TrimEnd() + "`n" + $dotSourceBlock + "`n"
                [System.IO.File]::WriteAllText($profilePath, $existingProfile, $enc)
                Write-Ok "Profile appended (existing content preserved): $profilePath"
            }
        } else {
            # No existing profile — create with just the block
            [System.IO.File]::WriteAllText($profilePath, $dotSourceBlock.TrimStart() + "`n", $enc)
            Write-Ok "Profile created: $profilePath"
        }
    }
}

# ================================================================
# Patch IDE shortcuts (run_command interception)
# ================================================================
if (-not $SkipShortcuts) {
    Write-Step "Detecting installed IDEs"

    $ides = Find-IDEInstalls
    if ($ides.Count -eq 0) {
        Write-Warn "No supported IDEs found. Supported:"
        $KnownIDEs | ForEach-Object { Write-Host "    - $($_.Name)" }
        Write-Warn "You can manually modify your IDE shortcut — see README.md"
    } else {
        foreach ($ide in $ides) {
            Write-Ok "Found: $($ide.Name) ($($ide.ExePath))"
            if ($ide.Shortcuts.Count -eq 0) {
                Write-Warn "  No shortcuts found — create one and re-run, or patch manually"
                continue
            }
            foreach ($lnk in $ide.Shortcuts) {
                Patch-Shortcut -LnkPath $lnk -BinDir $BinDir -ExePath $ide.ExePath
            }
        }
    }
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
Write-Host "  Launch via the patched shortcut to enable run_command interception."
Write-Host ""
Write-Host "  Controls:"
Write-Host "    Disable shim:  `$env:PWSH_SHIM_BYPASS = '1'"
Write-Host "    Debug mode:    `$env:PWSH_SHIM_DEBUG = '1'"
Write-Host "    Uninstall:     .\install.ps1 -Uninstall"
Write-Host ""
Write-Host "  Skip options:"
Write-Host "    -SkipBuild       Use pre-built binary"
Write-Host "    -SkipProfile     Don't install PS profile"
Write-Host "    -SkipShortcuts   Don't patch IDE shortcuts"
Write-Host ""
Write-Host "  Verify:          .\test.ps1"
Write-Host ""

