# shellfix - Test Suite
# Run from the repo root: .\test.ps1

param(
    [switch]$Verbose
)

$ErrorActionPreference = "Continue"
$pass = 0
$fail = 0
$skip = 0

function Test-Case {
    param(
        [string]$Name,
        [string]$Command,
        [string]$Expect,
        [switch]$UseShim,
        [switch]$SkipIfNoShim
    )
    
    if ($UseShim) {
        $shimPath = "$env:USERPROFILE\bin\powershell.exe"
        if (-not (Test-Path $shimPath)) {
            if ($SkipIfNoShim) {
                Write-Host "  SKIP: $Name (shim not installed)" -ForegroundColor Yellow
                $script:skip++
                return
            }
        }
        $output = & $shimPath -Command $Command 2>&1 | Out-String
    } else {
        $output = Invoke-Expression $Command 2>&1 | Out-String
    }
    
    if ($output -match $Expect) {
        Write-Host "  PASS: $Name" -ForegroundColor Green
        if ($Verbose) {
            $trimmed = $output.Trim()
            $len = [Math]::Min(60, $trimmed.Length)
            if ($len -gt 0) { Write-Host "        Output: $($trimmed.Substring(0, $len))" -ForegroundColor DarkGray }
        }
        $script:pass++
    } else {
        Write-Host "  FAIL: $Name" -ForegroundColor Red
        Write-Host "        Expected: /$Expect/" -ForegroundColor DarkGray
        $trimmed = $output.Trim()
        $len = [Math]::Min(80, $trimmed.Length)
        if ($len -gt 0) { Write-Host "        Got: $($trimmed.Substring(0, $len))" -ForegroundColor DarkGray }
        $script:fail++
    }
}

Write-Host ""
Write-Host "=============================================="
Write-Host "  shellfix - Test Suite"
Write-Host "=============================================="

# ================================================================
# Pre-flight
# ================================================================
Write-Host ""
Write-Host "--- Pre-flight ---"

$wslOk = $false
try {
    $r = wsl.exe -e echo ok 2>$null
    if ($r -match 'ok') { $wslOk = $true; Write-Host "  OK: WSL is running" -ForegroundColor Green }
} catch {}
if (-not $wslOk) {
    Write-Host "  FATAL: WSL is not available. Cannot run tests." -ForegroundColor Red
    exit 1
}

$shimPath = "$env:USERPROFILE\bin\powershell.exe"
$shimInstalled = Test-Path $shimPath
if ($shimInstalled) {
    Write-Host "  OK: Shim installed at $shimPath" -ForegroundColor Green
} else {
    Write-Host "  WARN: Shim not installed. Shim tests will be skipped." -ForegroundColor Yellow
}

. $PROFILE 2>$null
if ($env:PS_PROFILE_LOADED -eq "yes") {
    Write-Host "  OK: Profile loaded" -ForegroundColor Green
} else {
    Write-Host "  WARN: Profile not loaded. Profile tests may fail." -ForegroundColor Yellow
}

# Create test fixtures
wsl -e bash -c "printf 'it'\''s a test\nhere'\''s another\n' > /tmp/shellfix_test.txt"

# ================================================================
# CLASS 1: Bash Commands Through PowerShell
# ================================================================
Write-Host ""
Write-Host "--- Class 1: Bash -> WSL Routing (Shim) ---"
Test-Case "grep" 'grep -c "root" /etc/passwd' '\d+' -UseShim -SkipIfNoShim
Test-Case "head" 'head -1 /etc/os-release' '.' -UseShim -SkipIfNoShim
Test-Case "tail" 'tail -1 /etc/os-release' '.' -UseShim -SkipIfNoShim
Test-Case "wc" 'wc -l /etc/passwd' '\d+' -UseShim -SkipIfNoShim
Test-Case "uname" 'uname -s' 'Linux' -UseShim -SkipIfNoShim
Test-Case "date" 'date +%Y' '\d{4}' -UseShim -SkipIfNoShim

Write-Host ""
Write-Host "--- Class 1: Control Flow ---"
# These contain bash syntax that PS 5.1 cannot have as literals.
# We build strings at runtime using char codes.
if ($shimInstalled) {
    $a = [string][char]38   # &
    $p = [string][char]124  # |

    $cfTests = @(
        @{ n = 'if/then/fi'; c = 'if [ 1 -eq 1 ]; then echo yes; fi'; e = 'yes' }
    )
    # Build commands containing && and || at runtime
    $cfTests += @{ n = "$a$a operator"; c = "echo first $a$a echo second"; e = 'second' }
    $cfTests += @{ n = "$p$p operator"; c = "false $p$p echo fallback"; e = 'fallback' }

    foreach ($t in $cfTests) {
        $r = & $shimPath -Command $t.c 2>&1 | Out-String
        if ($r -match $t.e) {
            Write-Host "  PASS: $($t.n)" -ForegroundColor Green; $pass++
        } else {
            Write-Host "  FAIL: $($t.n)" -ForegroundColor Red; $fail++
        }
    }
} else {
    1..3 | ForEach-Object {
        Write-Host "  SKIP: control flow (shim not installed)" -ForegroundColor Yellow; $skip++
    }
}

Write-Host ""
Write-Host "--- Class 1: Quoting ---"
Test-Case "apostrophe" "grep `"it's`" /tmp/shellfix_test.txt" "it's a test" -UseShim -SkipIfNoShim
Test-Case "here's" "grep `"here's`" /tmp/shellfix_test.txt" "here's another" -UseShim -SkipIfNoShim

Write-Host ""
Write-Host "--- Class 1: Bash Wrappers (Profile) ---"
Test-Case "grep wrapper" 'grep -c "root" /etc/passwd' '\d+'
Test-Case "head wrapper" 'head -1 /etc/os-release' '.'
Test-Case "seq wrapper" 'seq 1 3' '2'
Test-Case "date wrapper" 'date +%Y' '\d{4}'

Write-Host ""
Write-Host "--- Class 1: Alias Deconfliction ---"
Test-Case "curl is Function" '(Get-Command curl).CommandType' 'Function'
Test-Case "diff is Function" '(Get-Command diff).CommandType' 'Function'
Test-Case "sort is Function" '(Get-Command sort).CommandType' 'Function'
Test-Case "cat not alias" '(Get-Command cat -ErrorAction SilentlyContinue).CommandType -ne "Alias"' 'True'

# ================================================================
# CLASS 2: Complex PS Quoting
# ================================================================
Write-Host ""
Write-Host "--- Class 2: PS Passthrough ---"
Test-Case "Write-Host" 'Write-Host "hello"' 'hello' -UseShim -SkipIfNoShim
Test-Case "Get-Date" 'Get-Date -Format yyyy' '\d{4}' -UseShim -SkipIfNoShim
Test-Case "PS variable" '$PSVersionTable.PSVersion.Major' '\d+' -UseShim -SkipIfNoShim

# ================================================================
# CLASS 3: NativeCommandError (stderr)
# ================================================================
Write-Host ""
Write-Host "--- Class 3: Native Tool Wrapping ---"

$nativeTests = @('git', 'gh', 'npm', 'dotnet')
foreach ($tool in $nativeTests) {
    $cmd = Get-Command $tool -ErrorAction SilentlyContinue
    if ($cmd) {
        if ($cmd.CommandType -eq 'Function') {
            Write-Host "  PASS: $tool is wrapped Function" -ForegroundColor Green
            $pass++
        } else {
            Write-Host "  FAIL: $tool is $($cmd.CommandType) (should be Function)" -ForegroundColor Red
            $fail++
        }
    } else {
        Write-Host "  SKIP: $tool not installed" -ForegroundColor Yellow
        $skip++
    }
}

Write-Host ""
Write-Host "--- Class 3: Clean Output ---"

$gitOut = git status 2>&1 | Out-String
if ($gitOut -notmatch 'NativeCommandError') {
    Write-Host "  PASS: git status - no NativeCommandError" -ForegroundColor Green
    $pass++
} else {
    Write-Host "  FAIL: git status - NativeCommandError present" -ForegroundColor Red
    $fail++
}

$ghOut = gh --version 2>&1 | Out-String
if ($ghOut -match 'gh version') {
    Write-Host "  PASS: gh --version - clean output" -ForegroundColor Green
    $pass++
} else {
    Write-Host "  FAIL: gh --version" -ForegroundColor Red
    $fail++
}

Write-Host ""
Write-Host "--- Class 3: Exit Code Propagation ---"

git log -1 --oneline 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "  PASS: git success = exit 0" -ForegroundColor Green
    $pass++
} else {
    Write-Host "  FAIL: git success = exit $LASTEXITCODE" -ForegroundColor Red
    $fail++
}

git log --oneline nonexistent-branch-xyz 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "  PASS: git failure = exit $LASTEXITCODE (non-zero)" -ForegroundColor Green
    $pass++
} else {
    Write-Host "  FAIL: git failure = exit 0 (expected non-zero)" -ForegroundColor Red
    $fail++
}

# ================================================================
# TIER 1: ANSI, dotnet, formatting
# ================================================================
Write-Host ""
Write-Host "--- Tier 1: ANSI Suppression ---"
Test-Case "NO_COLOR" '$env:NO_COLOR' '1'
Test-Case "TERM=dumb" '$env:TERM' 'dumb'
Test-Case "DOTNET_NOLOGO" '$env:DOTNET_NOLOGO' '1'

# Test ANSI stripping function
$ansiInput = "$([char]27)[31mERROR$([char]27)[0m: something failed"
$stripped = _shellfix_strip_ansi $ansiInput
if ($stripped -eq 'ERROR: something failed') {
    Write-Host "  PASS: ANSI strip function" -ForegroundColor Green; $pass++
} else {
    Write-Host "  FAIL: ANSI strip function (got: $stripped)" -ForegroundColor Red; $fail++
}

Write-Host ""
Write-Host "--- Tier 1: Output Formatting ---"
Test-Case "FormatEnumerationLimit" '$FormatEnumerationLimit' '-1'

# ================================================================
# Infrastructure
# ================================================================
Write-Host ""
Write-Host "--- Infrastructure ---"
Test-Case "WSL_UTF8" '$env:WSL_UTF8' '1'
Test-Case "WSLENV" '$env:WSLENV' 'PYTHONUTF8'
Test-Case "Profile loaded" '$env:PS_PROFILE_LOADED' 'yes'

$wh = Test-WslHealth
if ($wh) {
    Write-Host "  PASS: WSL healthy" -ForegroundColor Green; $pass++
} else {
    Write-Host "  FAIL: WSL unhealthy" -ForegroundColor Red; $fail++
}

# ================================================================
# Cleanup
# ================================================================
wsl -e rm -f /tmp/shellfix_test.txt 2>$null

$total = $pass + $fail
Write-Host ""
Write-Host "=============================================="
if ($fail -eq 0) {
    Write-Host "  PASS: $pass / $total" -ForegroundColor Green
} else {
    Write-Host "  PASS: $pass / $total" -ForegroundColor Red
}
if ($skip -gt 0) { Write-Host "  SKIP: $skip" -ForegroundColor Yellow }
if ($fail -gt 0) { Write-Host "  FAIL: $fail" -ForegroundColor Red }
Write-Host "=============================================="
Write-Host ""

exit $fail
