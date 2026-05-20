# wsl-shell-hardening — Test Suite
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
        if ($Verbose) { Write-Host "        Output: $($output.Trim().Substring(0, [Math]::Min(60, $output.Trim().Length)))" -ForegroundColor DarkGray }
        $script:pass++
    } else {
        Write-Host "  FAIL: $Name" -ForegroundColor Red
        Write-Host "        Expected: $Expect" -ForegroundColor DarkGray
        Write-Host "        Got: $($output.Trim().Substring(0, [Math]::Min(80, $output.Trim().Length)))" -ForegroundColor DarkGray
        $script:fail++
    }
}

Write-Host ""
Write-Host "=============================================="
Write-Host "  wsl-shell-hardening — Test Suite"
Write-Host "=============================================="

# ================================================================
# Pre-flight
# ================================================================
Write-Host "`n--- Pre-flight ---"

# WSL available?
$wslOk = $false
try {
    $r = wsl.exe -e echo ok 2>&1
    if ($r -match 'ok') { $wslOk = $true; Write-Host "  OK: WSL is running" -ForegroundColor Green }
} catch {}
if (-not $wslOk) {
    Write-Host "  FATAL: WSL is not available. Cannot run tests." -ForegroundColor Red
    exit 1
}

# Shim installed?
$shimPath = "$env:USERPROFILE\bin\powershell.exe"
$shimInstalled = Test-Path $shimPath
if ($shimInstalled) {
    Write-Host "  OK: Shim installed at $shimPath" -ForegroundColor Green
} else {
    Write-Host "  WARN: Shim not installed. Shim tests will be skipped." -ForegroundColor Yellow
}

# Profile loaded?
. $PROFILE 2>$null
if ($env:PS_PROFILE_LOADED -eq "yes") {
    Write-Host "  OK: Profile loaded" -ForegroundColor Green
} else {
    Write-Host "  WARN: Profile not loaded. Profile tests may fail." -ForegroundColor Yellow
}

# Create test fixture
wsl -e bash -c "echo 'it'\''s a test' > /tmp/wsh_test.txt && echo 'no apostrophe' >> /tmp/wsh_test.txt && echo 'here'\''s another' >> /tmp/wsh_test.txt"

# ================================================================
# Shim Tests
# ================================================================
Write-Host "`n--- Shim: Bash Routing ---"
Test-Case "grep -c" 'grep -c "class " /etc/passwd' '\d+' -UseShim -SkipIfNoShim
Test-Case "head" 'head -1 /etc/os-release' '.' -UseShim -SkipIfNoShim
Test-Case "tail" 'tail -1 /etc/os-release' '.' -UseShim -SkipIfNoShim
Test-Case "wc" 'wc -l /etc/passwd' '\d+' -UseShim -SkipIfNoShim
Test-Case "uname" 'uname -s' 'Linux' -UseShim -SkipIfNoShim
Test-Case "whoami" 'whoami' '\w+' -UseShim -SkipIfNoShim
Test-Case "date" 'date +%Y' '\d{4}' -UseShim -SkipIfNoShim

Write-Host "`n--- Shim: Previously Impossible ---"
Test-Case "for loop" 'for i in 1 2 3; do echo "num $i"; done' 'num 2' -UseShim -SkipIfNoShim
Test-Case "&& operator" 'echo "first" && echo "second"' 'second' -UseShim -SkipIfNoShim
Test-Case "|| operator" 'false || echo "fallback"' 'fallback' -UseShim -SkipIfNoShim
Test-Case "if/then/fi" 'if [ 1 -eq 1 ]; then echo "yes"; fi' 'yes' -UseShim -SkipIfNoShim

Write-Host "`n--- Shim: Quoting ---"
Test-Case "apostrophe (was HUNG)" "grep `"it's`" /tmp/wsh_test.txt" "it's a test" -UseShim -SkipIfNoShim
Test-Case "here's" "grep `"here's`" /tmp/wsh_test.txt" "here's another" -UseShim -SkipIfNoShim

Write-Host "`n--- Shim: PS Passthrough ---"
Test-Case "Write-Host" 'Write-Host "hello"' 'hello' -UseShim -SkipIfNoShim
Test-Case "Get-Date" 'Get-Date -Format yyyy' '\d{4}' -UseShim -SkipIfNoShim
Test-Case "PS variable" '$PSVersionTable.PSVersion.Major' '\d+' -UseShim -SkipIfNoShim

# ================================================================
# Profile Tests
# ================================================================
Write-Host "`n--- Profile: Wrappers ---"
Test-Case "grep wrapper" 'grep -c "root" /etc/passwd' '\d+'
Test-Case "head wrapper" 'head -1 /etc/os-release' '.'
Test-Case "tail wrapper" 'tail -1 /etc/os-release' '.'
Test-Case "wc wrapper" 'wc -l /etc/passwd' '\d+'
Test-Case "sort wrapper" 'echo "b`na`nc" | sort' 'a'
Test-Case "uniq wrapper" 'echo "a`na`nb" | uniq' 'a'
Test-Case "seq wrapper" 'seq 1 3' '2'
Test-Case "date wrapper" 'date +%Y' '\d{4}'

Write-Host "`n--- Profile: Dollar Sign Escaping ---"
Test-Case "awk `$1" 'echo "a b c" | awk ''{print $1, $3}''' 'a c'

Write-Host "`n--- Profile: Alias Deconfliction ---"
Test-Case "curl is Function" '(Get-Command curl).CommandType' 'Function'
Test-Case "diff is Function" '(Get-Command diff).CommandType' 'Function'
Test-Case "sort is Function" '(Get-Command sort).CommandType' 'Function'
Test-Case "cat is Function" '(Get-Command cat).CommandType' 'Function'

Write-Host "`n--- Profile: Environment ---"
Test-Case "WSL_UTF8" '$env:WSL_UTF8' '1'
Test-Case "WSLENV" '$env:WSLENV' 'PYTHONUTF8'
Test-Case "Profile marker" '$env:PS_PROFILE_LOADED' 'yes'

Write-Host "`n--- Profile: Health Checks ---"
Test-Case "WSL health" 'Test-WslHealth' 'True'

# ================================================================
# Exit Code Tests
# ================================================================
Write-Host "`n--- Exit Codes ---"
grep -q "root" /etc/passwd
$exitFound = $LASTEXITCODE
grep -q "NONEXISTENT_XYZ_12345" /etc/passwd 2>$null
$exitNotFound = $LASTEXITCODE

if ($exitFound -eq 0) { Write-Host "  PASS: grep found → exit 0" -ForegroundColor Green; $pass++ }
else { Write-Host "  FAIL: grep found → exit $exitFound (expected 0)" -ForegroundColor Red; $fail++ }

if ($exitNotFound -ne 0) { Write-Host "  PASS: grep not found → exit $exitNotFound" -ForegroundColor Green; $pass++ }
else { Write-Host "  FAIL: grep not found → exit 0 (expected non-zero)" -ForegroundColor Red; $fail++ }

# ================================================================
# Cleanup & Summary
# ================================================================
wsl -e rm -f /tmp/wsh_test.txt 2>$null

Write-Host ""
Write-Host "=============================================="
$total = $pass + $fail
$color = if ($fail -eq 0) { "Green" } else { "Red" }
Write-Host "  PASS: $pass / $total" -ForegroundColor $color
if ($skip -gt 0) { Write-Host "  SKIP: $skip" -ForegroundColor Yellow }
if ($fail -gt 0) { Write-Host "  FAIL: $fail" -ForegroundColor Red }
Write-Host "=============================================="
Write-Host ""

exit $fail
