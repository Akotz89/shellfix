# === shellfix — PowerShell Profile ===
# Loaded automatically on every agent command (powershell -Command).
# Layer 2: Bash wrappers | Layer 3: NativeCommandError suppression

# --- UTF-8 Encoding ---
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8NoBom
[Console]::OutputEncoding = $utf8NoBom
$OutputEncoding = $utf8NoBom
$env:PYTHONUTF8 = "1"
$env:PYTHONIOENCODING = "utf-8"
$env:WSL_UTF8 = "1"

# --- ANSI / Color Suppression ---
# AI agents run in non-interactive terminals and can't parse ANSI escape
# codes (colors, progress bars, spinners). These appear as garbled text
# like [31m or ←[0m. Suppress them at the source.
$env:NO_COLOR = "1"              # https://no-color.org — respected by most modern CLIs
$env:TERM = "dumb"               # tells tools not to emit escape sequences
$env:DOTNET_NOLOGO = "1"         # suppress .NET welcome banner
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"  # suppress telemetry notice

# --- Output Formatting ---
# PS default Format-Table truncates columns and limits enum display.
# Agents see '...' instead of full values and make wrong decisions.
$FormatEnumerationLimit = -1
$PSDefaultParameterValues['Format-Table:AutoSize'] = $true

# --- WSLENV: pass common env vars to WSL ---
# /p = translate as path, /u = pass as-is (unix)
# Only add vars that agents commonly set/check
$wslenvParts = @(
    'PYTHONUTF8/u',
    'PYTHONIOENCODING/u',
    'NODE_ENV/u',
    'CI/u',
    'TERM/u'
)
$existing = $env:WSLENV
if ($existing) {
    $wslenvParts = ($existing -split ':') + $wslenvParts
}
$env:WSLENV = ($wslenvParts | Select-Object -Unique) -join ':'

# --- Path Translation: Windows paths -> WSL paths ---
function global:Convert-ToWslPath {
    param([string]$p)
    if ($p -match '^([A-Za-z]):[\\\/]') {
        $d = $Matches[1].ToLower()
        return "/mnt/$d" + ($p.Substring(2) -replace '\\','/')
    }
    if ($p -match '^\\\\wsl') {
        $p = $p -replace '^\\\\wsl[^\\/]*\\[^\\/]+',''
        return ($p -replace '\\','/')
    }
    return $p
}

# --- Quote a single argument for bash -c ---
function global:Quote-ForBash {
    param([string]$s)
    # Escape $ so bash -c doesn't expand (awk '{print $1}')
    $s = $s -replace '\$', '\$'
    # Single-quote wrap for any special characters
    if ($s -match '[\s*?\[\]{}()\\!#&;|<>~`"'']') {
        # Replace single quotes with '\'' (close, escaped, reopen)
        $escaped = $s -replace "'", "'\''"
        return "'$escaped'"
    }
    return $s
}

# --- Build bash command string from name + PS args ---
function global:Build-BashCmd {
    param([string]$Name, [object[]]$Arguments)
    $parts = @()
    foreach ($a in $Arguments) {
        $converted = Convert-ToWslPath ([string]$a)
        $parts += Quote-ForBash $converted
    }
    return "$Name $($parts -join ' ')"
}

# --- Remove conflicting PowerShell aliases ---
# curl → Invoke-WebRequest is particularly dangerous
@('diff', 'sort', 'tee', 'cat', 'head', 'tail', 'find', 'file', 'curl') | ForEach-Object {
    Remove-Item "Alias:$_" -Force -ErrorAction SilentlyContinue
}

# --- Direct WSL Function Wrappers ---
$wslCommands = @(
    'grep', 'sed', 'awk', 'head', 'tail', 'wc', 'sort', 'uniq',
    'cut', 'tr', 'xargs', 'tee',
    'find', 'file', 'stat', 'realpath', 'basename', 'dirname',
    'chmod', 'chown', 'ln', 'readlink', 'touch',
    'diff', 'patch',
    'md5sum', 'sha256sum',
    'uname', 'whoami', 'hostname', 'id', 'printenv',
    'curl', 'wget', 'jq',
    'tar', 'gzip', 'gunzip', 'zip', 'unzip',
    'xxd', 'od', 'strings', 'seq', 'date', 'cal'
)

foreach ($cmd in $wslCommands) {
    $sb = [scriptblock]::Create(@"
    begin { `$_lines = [System.Collections.Generic.List[string]]::new(); `$_piped = `$false }
    process { if (`$_ -ne `$null) { `$_piped = `$true; `$_lines.Add([string]`$_) } }
    end {
        `$fc = Build-BashCmd '$cmd' `$args
        `$fc = `$fc -replace '"', '\"'
        if (`$_piped) { `$_lines | wsl.exe -d Ubuntu-24.04 -- bash -c `$fc }
        else { wsl.exe -d Ubuntu-24.04 -- bash -c `$fc }
    }
"@)
    New-Item -Path "function:global:$cmd" -Value $sb -Force | Out-Null
}

# --- WSL Bash Fallback ---
$ExecutionContext.InvokeCommand.CommandNotFoundAction = {
    param([string]$commandName, [System.Management.Automation.CommandLookupEventArgs]$eventArgs)
    $realName = $commandName
    if ($commandName -match '^Get-(.+)$') { $realName = $Matches[1] }
    if ($realName -match '-' -and $realName -notmatch '^Get-') { return }
    try {
        $wslResult = wsl.exe -d Ubuntu-24.04 -e which $realName 2>$null
        if ($LASTEXITCODE -eq 0 -and $wslResult) {
            $eventArgs.StopSearch = $true
            $cmdToRun = $realName
            $eventArgs.CommandScriptBlock = {
                $fc = Build-BashCmd $cmdToRun $args
                $fc = $fc -replace '"', '\"'
                wsl.exe -d Ubuntu-24.04 -- bash -c $fc
            }.GetNewClosure()
        }
    } catch {}
}

# --- WSL Health Guard ---
# Quick check that WSL is responsive; warn if not
function global:Test-WslHealth {
    try {
        $r = wsl.exe -d Ubuntu-24.04 -e echo ok 2>$null
        if ($r -match 'ok') { return $true }
    } catch {}
    Write-Warning "[SHELL] WSL is not responding. Bash commands will fail."
    Write-Warning "[SHELL] Fix: wsl --shutdown ; wsl -d Ubuntu-24.04"
    return $false
}

# --- Shim PATH Guard ---
# Verify our shim is first in PATH (only warn, don't break)
function global:Test-ShimPath {
    $shimPath = "C:\Users\Aaron\bin\powershell.exe"
    if (Test-Path $shimPath) {
        $resolved = (Get-Command powershell.exe -ErrorAction SilentlyContinue).Source
        if ($resolved -and $resolved -ne $shimPath) {
            Write-Warning "[SHELL] Shim not first in PATH. Got: $resolved"
            Write-Warning "[SHELL] Expected: $shimPath"
            return $false
        }
    }
    return $true
}

# --- NativeCommandError Suppression + ANSI Stripping ---
# PS 5.1 treats ANY stderr output from native executables as an error.
# git, npm, dotnet, gh all write progress/warnings/info to stderr.
# This makes agents think commands failed when they succeeded.
# Fix: wrap common tools to merge stderr to stdout as plain strings,
# and strip any remaining ANSI escape codes from the output.

# ANSI escape code regex: matches CSI sequences like \e[31m, \e[0m, etc.
$global:_shellfix_ansi_regex = [regex]'\x1b\[[0-9;]*[A-Za-z]'

function global:_shellfix_strip_ansi {
    param([string]$s)
    return $global:_shellfix_ansi_regex.Replace($s, '')
}

$nativeTools = @('git', 'npm', 'npx', 'dotnet', 'gh', 'cargo', 'rustc', 'docker', 'kubectl')

foreach ($tool in $nativeTools) {
    # Only wrap if the tool exists as a real executable (not already a function)
    $existing = Get-Command $tool -CommandType Application -ErrorAction SilentlyContinue
    if ($existing) {
        $exePath = $existing.Source
        # dotnet gets special treatment: inject --tl:off to disable Terminal Logger
        # which uses ANSI cursor movement that agents can't parse
        if ($tool -eq 'dotnet') {
            $sb = [scriptblock]::Create(@"
                `$oldEAP = `$ErrorActionPreference
                `$ErrorActionPreference = 'Continue'
                try {
                    # Inject --tl:off if not already present and command is build/test/run/publish
                    `$injectedArgs = @(`$args)
                    `$firstArg = if (`$args.Count -gt 0) { `$args[0] } else { '' }
                    `$tlCommands = @('build', 'test', 'run', 'publish', 'pack', 'restore')
                    if (`$tlCommands -contains `$firstArg -and `$args -notcontains '--tl:off') {
                        `$injectedArgs += '--tl:off'
                    }
                    & '$exePath' @injectedArgs 2>&1 | ForEach-Object {
                        if (`$_ -is [System.Management.Automation.ErrorRecord]) {
                            _shellfix_strip_ansi `$_.Exception.Message
                        } else {
                            _shellfix_strip_ansi ([string]`$_)
                        }
                    }
                } finally {
                    `$ErrorActionPreference = `$oldEAP
                }
"@)
        } else {
            $sb = [scriptblock]::Create(@"
                `$oldEAP = `$ErrorActionPreference
                `$ErrorActionPreference = 'Continue'
                try {
                    & '$exePath' @args 2>&1 | ForEach-Object {
                        if (`$_ -is [System.Management.Automation.ErrorRecord]) {
                            _shellfix_strip_ansi `$_.Exception.Message
                        } else {
                            _shellfix_strip_ansi ([string]`$_)
                        }
                    }
                } finally {
                    `$ErrorActionPreference = `$oldEAP
                }
"@)
        }
        New-Item -Path "function:global:$tool" -Value $sb -Force | Out-Null
    }
}

# --- BOM-Safe File Writing ---
# PS 5.1's Set-Content defaults to system ANSI, Out-File defaults to UTF-16LE.
# Both corrupt JSON, YAML, and config files that agents write.
# Override default encoding to UTF-8 no-BOM via PSDefaultParameterValues.
# Users can still pass explicit -Encoding to override.
$PSDefaultParameterValues['Set-Content:Encoding'] = 'UTF8'
$PSDefaultParameterValues['Out-File:Encoding'] = 'UTF8'
$PSDefaultParameterValues['Add-Content:Encoding'] = 'UTF8'

# Note: PS 5.1's -Encoding UTF8 still adds a BOM. For truly BOM-free writes,
# the profile provides a helper function agents can use directly.
function global:Write-Utf8NoBom {
    <#
    .SYNOPSIS
        Write text to a file as UTF-8 without BOM.
    .DESCRIPTION
        PS 5.1 has no built-in way to write UTF-8 without BOM.
        This function uses .NET directly to avoid the BOM.
    .PARAMETER Path
        Path to the file to write.
    .PARAMETER Content
        String content to write.
    .PARAMETER Append
        Append to existing file instead of overwriting.
    #>
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory, ValueFromPipeline)][string]$Content,
        [switch]$Append
    )
    $resolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    $enc = [System.Text.UTF8Encoding]::new($false)
    if ($Append -and (Test-Path $resolved)) {
        $existing = [System.IO.File]::ReadAllText($resolved, $enc)
        [System.IO.File]::WriteAllText($resolved, $existing + $Content, $enc)
    } else {
        [System.IO.File]::WriteAllText($resolved, $Content, $enc)
    }
}

# --- Shell Integration Compatibility ---
# VS Code injects shell integration markers into the PS profile via a
# ScriptBlock that modifies the prompt function. This can conflict with
# custom profiles and cause infinite loops or hangs.
# Guard: if VS Code shell integration is active, don't redefine prompt.
# We detect it by checking for the TERM_PROGRAM variable.
if ($env:TERM_PROGRAM -eq 'vscode') {
    # VS Code manages the prompt; don't interfere.
    # Ensure our profile doesn't define a conflicting prompt function.
    # The env vars and wrappers still load fine.
} else {
    # Outside VS Code, set a minimal prompt that doesn't conflict
    function global:prompt {
        "PS $($executionContext.SessionState.Path.CurrentLocation)> "
    }
}

$env:PS_PROFILE_LOADED = "yes"
