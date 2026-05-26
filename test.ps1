# shellfix - Test Suite
# Run from the repo root: .\test.ps1

param(
    [string]$ShimPath,
    [string]$WslDistro = "Ubuntu-24.04",
    [switch]$Verbose
)

$ErrorActionPreference = "Continue"
$pass = 0
$fail = 0
$skip = 0
$originalShellfixWslDistro = $env:SHELLFIX_WSL_DISTRO
$env:SHELLFIX_WSL_DISTRO = $WslDistro

# --- Resolve shim path: repo-local build > installed > skip ---
if (-not $ShimPath) {
    $repoLocal = Join-Path $PSScriptRoot "shim\out\powershell.exe"
    $installed = Join-Path $env:USERPROFILE "bin\powershell.exe"
    if (Test-Path $repoLocal) {
        $ShimPath = $repoLocal
    } elseif (Test-Path $installed) {
        $ShimPath = $installed
    }
}
$script:ShimPath = $ShimPath

function Test-Case {
    param(
        [string]$Name,
        [string]$Command,
        [string]$Expect,
        [switch]$UseShim,
        [switch]$SkipIfNoShim
    )
    
    if ($UseShim) {
        if (-not $script:ShimPath -or -not (Test-Path $script:ShimPath)) {
            if ($SkipIfNoShim) {
                Write-Host "  SKIP: $Name (shim not found)" -ForegroundColor Yellow
                $script:skip++
                return
            }
        }
        $shimExe = $script:ShimPath
        # Use ProcessStartInfo.ArgumentList to preserve command boundaries.
        # Start-Process -ArgumentList flattens multiline strings and can mangle
        # exactly the inline payloads shellfix is meant to protect.
        try {
            $psi = [System.Diagnostics.ProcessStartInfo]::new()
            $psi.FileName = (Resolve-Path $shimExe)
            $psi.UseShellExecute = $false
            $psi.RedirectStandardOutput = $true
            $psi.RedirectStandardError = $true
            $psi.ArgumentList.Add("-NoProfile")
            $psi.ArgumentList.Add("-Command")
            $psi.ArgumentList.Add($Command)
            $proc = [System.Diagnostics.Process]::Start($psi)
            $stdout = $proc.StandardOutput.ReadToEnd()
            $stderr = $proc.StandardError.ReadToEnd()
            $proc.WaitForExit()
            $output = $stdout + $stderr
        } catch {
            $output = $_ | Out-String
        }
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
    $r = wsl.exe -d $WslDistro -e echo ok 2>$null
    if ($r -match 'ok') { $wslOk = $true; Write-Host "  OK: WSL is running" -ForegroundColor Green }
} catch {}
if (-not $wslOk) {
    Write-Host "  FATAL: WSL is not available. Cannot run tests." -ForegroundColor Red
    exit 1
}

$shimInstalled = $script:ShimPath -and (Test-Path $script:ShimPath)
if ($shimInstalled) {
    $shimItem = Get-Item $script:ShimPath
    $shimHash = (Get-FileHash $script:ShimPath -Algorithm SHA256).Hash.Substring(0, 12)
    Write-Host "  OK: Shim under test: $script:ShimPath" -ForegroundColor Green
    Write-Host "      Built: $($shimItem.LastWriteTime)  SHA256: $shimHash..." -ForegroundColor DarkGray
    Write-Host "      WSL distro: $WslDistro" -ForegroundColor DarkGray
} else {
    Write-Host "  WARN: Shim not found. Shim tests will be skipped." -ForegroundColor Yellow
}

$profileUnderTest = Join-Path $PSScriptRoot "profile\Microsoft.PowerShell_profile.ps1"
if (-not (Test-Path $profileUnderTest)) {
    $profileUnderTest = $PROFILE
}
. $profileUnderTest 2>$null
if ($env:PS_PROFILE_LOADED -eq "yes") {
    Write-Host "  OK: Profile loaded: $profileUnderTest" -ForegroundColor Green
} else {
    Write-Host "  WARN: Profile not loaded. Profile tests may fail." -ForegroundColor Yellow
}

# Create test fixtures
$fixtureScript = @'
printf "%s\n%s\n" "it's a test" "here's another" > /tmp/shellfix_test.txt
'@.Trim()
wsl.exe -d $WslDistro -- bash -lc $fixtureScript

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
        $r = & $script:ShimPath -Command $t.c 2>&1 | Out-String
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

if (Get-Command npx -CommandType Application -ErrorAction SilentlyContinue) {
    Test-Case "where resolves npx as Windows tool" 'where npx' 'npx(\.cmd)?' -UseShim -SkipIfNoShim
    Test-Case "npx wrapper executes single native path" 'npx --version' '\d+\.\d+\.\d+' -UseShim -SkipIfNoShim
} else {
    Write-Host "  SKIP: npx wrapper regression (npx not installed)" -ForegroundColor Yellow
    $skip++
}

if (Get-Command python -CommandType Application -ErrorAction SilentlyContinue) {
    Test-Case "python inline stays native" 'python -c "import sys; print(sys.executable)"' '^[A-Za-z]:\\' -UseShim -SkipIfNoShim
    $nativePythonRegex = @'
python -c "import re
for m in re.finditer(r'https?://[^\s\'\",)]+', 'https://example.com/path'):
    print(m.group())
"
'@.Trim()
    Test-Case "python multiline regex avoids PowerShell parser" $nativePythonRegex 'https://example.com/path' -UseShim -SkipIfNoShim
} else {
    Write-Host "  SKIP: python native inline regression (python not installed)" -ForegroundColor Yellow
    $skip++
}

if (Get-Command python3 -CommandType Application -ErrorAction SilentlyContinue) {
    Test-Case "python3 inline stays native" 'python3 -c "import sys; print(sys.executable)"' '^[A-Za-z]:\\' -UseShim -SkipIfNoShim
} else {
    Write-Host "  SKIP: python3 native inline regression (python3 not installed)" -ForegroundColor Yellow
    $skip++
}

if (Get-Command node -CommandType Application -ErrorAction SilentlyContinue) {
    Test-Case "node inline stays native" 'node -e "console.log(process.execPath)"' '^[A-Za-z]:\\' -UseShim -SkipIfNoShim
} else {
    Write-Host "  SKIP: node native inline regression (node not installed)" -ForegroundColor Yellow
    $skip++
}

$d2Command = Get-Command d2 -CommandType Application -ErrorAction SilentlyContinue
if (-not $d2Command -and (Test-Path 'C:\Program Files\D2\d2.exe')) {
    $d2Command = Get-Item 'C:\Program Files\D2\d2.exe'
}
if ($d2Command) {
    Test-Case "d2 resolves after PATH refresh" 'd2 --version' 'v\d+\.\d+\.\d+' -UseShim -SkipIfNoShim
    $d2Path = (where.exe d2 2>$null | Select-Object -First 1)
    if (-not $d2Path -and (Test-Path 'C:\Program Files\D2\d2.exe')) {
        $d2Path = 'C:\Program Files\D2\d2.exe'
    }
    $d2Input = Join-Path ([System.IO.Path]::GetTempPath()) "shellfix_test_d2_$([guid]::NewGuid().ToString('N')).d2"
    $d2Output = [System.IO.Path]::ChangeExtension($d2Input, ".png")
    try {
        Set-Content -LiteralPath $d2Input -Value "x -> y" -Encoding UTF8
        Test-Case "d2 full-path direct avoids NativeCommandError" "& `"$d2Path`" `"$d2Input`" `"$d2Output`" 2>&1" 'success: successfully compiled' -UseShim -SkipIfNoShim
    } finally {
        Remove-Item -LiteralPath $d2Input, $d2Output -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "  SKIP: d2 PATH refresh regression (d2 not installed)" -ForegroundColor Yellow
    $skip++
}

$dotPath = (where.exe dot 2>$null | Select-Object -First 1)
if (-not $dotPath -and (Test-Path 'C:\Program Files\Graphviz\bin\dot.exe')) {
    $dotPath = 'C:\Program Files\Graphviz\bin\dot.exe'
}
if ($dotPath) {
    Test-Case "Graphviz dot full-path direct avoids NativeCommandError" "& `"$dotPath`" -V 2>&1" 'graphviz version' -UseShim -SkipIfNoShim
} else {
    Write-Host "  SKIP: Graphviz dot stderr regression (dot not installed)" -ForegroundColor Yellow
    $skip++
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
# TIER 2: BOM-safe, LongPaths, Shell Integration
# ================================================================
Write-Host ""
Write-Host "--- Tier 2: BOM-Safe File Writing ---"
Test-Case "Set-Content default UTF8" '$PSDefaultParameterValues["Set-Content:Encoding"]' 'UTF8'
Test-Case "Out-File default UTF8" '$PSDefaultParameterValues["Out-File:Encoding"]' 'UTF8'
Test-Case "Add-Content default UTF8" '$PSDefaultParameterValues["Add-Content:Encoding"]' 'UTF8'

# Test Write-Utf8NoBom helper
$testFile = Join-Path $env:TEMP "shellfix_bom_test.txt"
Write-Utf8NoBom -Path $testFile -Content "hello world"
$bytes = [System.IO.File]::ReadAllBytes($testFile)
# BOM would be EF BB BF (239 187 191). Check first 3 bytes are NOT the BOM.
if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
    Write-Host "  FAIL: Write-Utf8NoBom has BOM" -ForegroundColor Red; $fail++
} else {
    $content = [System.IO.File]::ReadAllText($testFile)
    if ($content -eq "hello world") {
        Write-Host "  PASS: Write-Utf8NoBom (no BOM, correct content)" -ForegroundColor Green; $pass++
    } else {
        Write-Host "  FAIL: Write-Utf8NoBom content mismatch" -ForegroundColor Red; $fail++
    }
}
Remove-Item $testFile -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "--- Tier 2: Shell Integration ---"
# Verify prompt function exists and doesn't conflict
$promptCmd = Get-Command prompt -ErrorAction SilentlyContinue
if ($promptCmd) {
    Write-Host "  PASS: prompt function exists" -ForegroundColor Green; $pass++
} else {
    Write-Host "  FAIL: prompt function missing" -ForegroundColor Red; $fail++
}

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
# Issue Regression Tests (Shim Layer)
# These tests invoke the shim binary directly via -UseShim,
# which reads raw command line and bypasses PS parsing.
# ================================================================
Write-Host "`nIssue Regression Tests (Shim):" -ForegroundColor Cyan

# Issue #1: && in wsl bash -c
Test-Case "Issue #1: && in bash -c" `
    "wsl -d $WslDistro -- bash -c `"echo hello && echo world`"" `
    'hello' -UseShim -SkipIfNoShim

# Issue #2: Python [1:-1] slice syntax
Test-Case "Issue #2: Python slice [1:-1]" `
    ('wsl -d {0} -- bash -c "python3 -c ''print(list(range(5))[1:-1])''"' -f $WslDistro) `
    '\[1.*2.*3\]' -UseShim -SkipIfNoShim

# Issue #3: Nested single quotes inside a bash -c payload
Test-Case "Issue #3: Nested quotes python" `
    ('wsl -d {0} -- bash -c "python3 -c ''print(1)''"' -f $WslDistro) `
    '1' -UseShim -SkipIfNoShim

# Issue #1 variant: multi-command chain
Test-Case "Issue #1 variant: triple &&" `
    "wsl -d $WslDistro -- bash -c `"echo one && echo two && echo three`"" `
    'three' -UseShim -SkipIfNoShim

# Issue #4: $variable inside bash -c — PS expands $var to empty
# before bash ever sees it, silently destroying loop variables
Test-Case "Issue #4: `$var in bash -c for loop" `
    "wsl -d $WslDistro -- bash -c `"for x in hello world; do echo item=`$x; done`"" `
    'item=hello' -UseShim -SkipIfNoShim

Test-Case "Issue #4: `$var in bash -c assignment" `
    "wsl -d $WslDistro -- bash -c `"port=9999; echo port=`$port`"" `
    'port=9999' -UseShim -SkipIfNoShim

$wslMultilinePython = @'
wsl -d __DISTRO__ -- bash -c "cd /tmp && python3 -c \"
print('before conversion')
def svg2png(url, write_to, output_width, output_height):
    print('Converted SVG -> PNG at %sx%s' % (output_width, output_height))
svg2png(
    url='hipaa_final.svg',
    write_to='hipaa_final.png',
    output_width=3000,
    output_height=2250
)
\""
'@.Trim().Replace('__DISTRO__', $WslDistro)
Test-Case "Issue #5: WSL multiline python payload" `
    $wslMultilinePython `
    'Converted SVG -> PNG at 3000x2250' -UseShim -SkipIfNoShim

Test-Case "Issue #6: WSL `$PATH colon token" `
    "wsl -d $WslDistro -- bash -c `"echo `$PATH:/usr/local/bin`"" `
    '/usr/local/bin' -UseShim -SkipIfNoShim

Test-Case "Issue #7: WSL escaped `$HOME PATH export" `
    "wsl -d $WslDistro -- bash -c `"export PATH=\`$HOME/.local/bin:\`$PATH && echo HOME=\`$HOME`"" `
    'HOME=/home/' -UseShim -SkipIfNoShim

Test-Case "Issue #7: WSL literal `$HOME path lookup" `
    "wsl -d $WslDistro -- bash -c `"test -d `$HOME && echo HOME_OK=`$HOME`"" `
    'HOME_OK=/home/' -UseShim -SkipIfNoShim

# ================================================================
# Cleanup
# ================================================================
wsl.exe -d $WslDistro -e rm -f /tmp/shellfix_test.txt 2>$null

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

if ($null -eq $originalShellfixWslDistro) {
    Remove-Item Env:SHELLFIX_WSL_DISTRO -ErrorAction SilentlyContinue
} else {
    $env:SHELLFIX_WSL_DISTRO = $originalShellfixWslDistro
}

exit $fail
