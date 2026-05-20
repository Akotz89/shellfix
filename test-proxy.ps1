# shellfix - Proxy Mode Test Suite v2
# Uses pipe-based testing instead of Process.Start
#
# Usage: .\test-proxy.ps1 [-ShimPath <path>] [-WslDistro <name>] [-Verbose]

param(
    [string]$ShimPath,
    [string]$WslDistro = "Ubuntu-24.04",
    [switch]$Verbose
)

$ErrorActionPreference = "Continue"
$pass = 0
$fail = 0
$originalShellfixWslDistro = $env:SHELLFIX_WSL_DISTRO
$env:SHELLFIX_WSL_DISTRO = $WslDistro

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

if (-not $script:ShimPath -or -not (Test-Path $script:ShimPath)) {
    Write-Host "FATAL: Shim not found. Build first or pass -ShimPath." -ForegroundColor Red
    exit 1
}

function Run-ProxyTest {
    param(
        [string]$Name,
        [string]$Command,
        [string]$ExpectPattern
    )

    # Build input: the command + exit
    $testInput = "$Command`nexit`n"

    # Run through shim via pipe - capture stdout and stderr separately
    $tmpOut = [System.IO.Path]::GetTempFileName()
    $tmpErr = [System.IO.Path]::GetTempFileName()

    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $script:ShimPath
        $psi.Arguments = "-NoProfile -NoLogo"
        $psi.UseShellExecute = $false
        $psi.RedirectStandardInput = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.Environment["PWSH_SHIM_DEBUG"] = if ($Verbose) { "1" } else { "0" }

        $proc = [System.Diagnostics.Process]::Start($psi)

        # Start async output readers
        $outTask = $proc.StandardOutput.ReadToEndAsync()
        $errTask = $proc.StandardError.ReadToEndAsync()

        # Wait for PS to initialize inside the proxy
        Start-Sleep -Milliseconds 2500

        # Send command and exit
        $proc.StandardInput.WriteLine($Command)
        $proc.StandardInput.Flush()
        Start-Sleep -Milliseconds 500
        $proc.StandardInput.WriteLine("exit")
        $proc.StandardInput.Flush()
        Start-Sleep -Milliseconds 200
        try { $proc.StandardInput.Close() } catch {}

        if (-not $proc.WaitForExit(15000)) {
            $proc.Kill()
            Write-Host "  FAIL: $Name (TIMEOUT)" -ForegroundColor Red
            $script:fail++
            return
        }

        $stdout = $outTask.GetAwaiter().GetResult()
        $stderr = $errTask.GetAwaiter().GetResult()

        if ($stdout -match $ExpectPattern) {
            Write-Host "  PASS: $Name" -ForegroundColor Green
            if ($Verbose) {
                $debugLines = $stderr -split "`n" | Where-Object { $_ -match 'SHIM-PROXY' }
                foreach ($dl in $debugLines) {
                    Write-Host "        $($dl.Trim())" -ForegroundColor DarkGray
                }
            }
            $script:pass++
        } else {
            Write-Host "  FAIL: $Name" -ForegroundColor Red
            Write-Host "        Expected: /$ExpectPattern/" -ForegroundColor DarkGray
            $snippet = ($stdout -split "`n" | Where-Object { $_.Trim() -ne "" } | Select-Object -First 5) -join " | "
            if ($snippet.Length -gt 120) { $snippet = $snippet.Substring(0, 120) + "..." }
            Write-Host "        Got: $snippet" -ForegroundColor DarkGray
            $script:fail++
        }
    } finally {
        Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "============================================="
Write-Host "  shellfix Proxy Mode Tests v2"
$shimItem = Get-Item $script:ShimPath
$shimHash = (Get-FileHash $script:ShimPath -Algorithm SHA256).Hash.Substring(0, 12)
Write-Host "  Shim: $script:ShimPath"
Write-Host "  Built: $($shimItem.LastWriteTime)  SHA256: $shimHash..."
Write-Host "  WSL distro: $WslDistro"
Write-Host "============================================="
Write-Host ""

# --- Pure PS regression ---
Write-Host "Pure PowerShell (regression):" -ForegroundColor Cyan
Run-ProxyTest "echo string" 'echo "hello world"' 'hello world'
Run-ProxyTest "Get-Date" 'Get-Date -Format yyyy' '202\d'
Run-ProxyTest "PS variable" '$PSVersionTable.PSVersion.Major' '5'
Run-ProxyTest "PS semicolon" 'echo "one"; echo "two"; echo "three"' 'three'

# --- WSL safe ---
Write-Host "`nWSL safe (no rewrite):" -ForegroundColor Cyan
Run-ProxyTest "wsl echo" "wsl -d $WslDistro -- echo `"hello wsl`"" 'hello wsl'
Run-ProxyTest "wsl uname" "wsl -d $WslDistro -- uname -s" 'Linux'

# --- Issue #1: && ---
Write-Host "`nIssue #1 (&&):" -ForegroundColor Cyan
Run-ProxyTest "basic &&" "wsl -d $WslDistro -- bash -c `"echo hello && echo world`"" 'hello[\s\S]*world'
Run-ProxyTest "triple &&" "wsl -d $WslDistro -- bash -c `"echo a && echo b && echo c`"" 'a[\s\S]*b[\s\S]*c'
Run-ProxyTest "|| fallback" "wsl -d $WslDistro -- bash -c `"false || echo fallback`"" 'fallback'
Run-ProxyTest "&& with cd" "wsl -d $WslDistro -- bash -c `"cd /tmp && pwd`"" '/tmp'

# --- Issue #2: [N:-N] ---
Write-Host "`nIssue #2 ([N:-N]):" -ForegroundColor Cyan
Run-ProxyTest "python slice" "wsl -d $WslDistro -- bash -c ""python3 -c 'print(list(range(5))[1:-1])'""" '\[1.*2.*3\]'
Run-ProxyTest "string slice" "wsl -d $WslDistro -- bash -c ""python3 -c 'x=`"abcde`"; print(x[1:-1])'""" 'bcd'

# --- Issue #3: Nested quotes ---
Write-Host "`nIssue #3 (nested quotes):" -ForegroundColor Cyan
Run-ProxyTest "single in double" "wsl -d $WslDistro -- bash -c ""echo 'hello world'""" 'hello world'
Run-ProxyTest "python in bash" "wsl -d $WslDistro -- bash -c ""python3 -c 'print(1+2)'""" '3'

# --- Regression: false positives ---
Write-Host "`nRegression (no false rewrite):" -ForegroundColor Cyan
Run-ProxyTest "PS Write-Output" 'Write-Output "testing done"' 'testing done'
Run-ProxyTest "PS env var" 'Get-ChildItem env:USERPROFILE | Select-Object -ExpandProperty Value' ([regex]::Escape($env:USERPROFILE))

# --- Summary ---
$total = $pass + $fail
Write-Host ""
Write-Host "============================================="
if ($fail -eq 0) {
    Write-Host "  ALL PASS: $pass / $total" -ForegroundColor Green
} else {
    Write-Host "  PASS: $pass / $total, FAIL: $fail" -ForegroundColor Red
}
Write-Host "============================================="
Write-Host ""

if ($null -eq $originalShellfixWslDistro) {
    Remove-Item Env:SHELLFIX_WSL_DISTRO -ErrorAction SilentlyContinue
} else {
    $env:SHELLFIX_WSL_DISTRO = $originalShellfixWslDistro
}

exit $fail
