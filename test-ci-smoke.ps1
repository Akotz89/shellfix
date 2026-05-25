# shellfix - CI smoke tests
# Runs against the freshly built shim. Intended for GitHub Actions and quick local checks.

param(
    [string]$ShimPath = (Join-Path $PSScriptRoot "shim\out\powershell.exe"),
    [string]$WslDistro = "Ubuntu-24.04"
)

$ErrorActionPreference = "Continue"
$pass = 0
$fail = 0
$skip = 0

function Invoke-ShimCommand {
    param(
        [string]$Command,
        [switch]$DebugShim
    )

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = (Resolve-Path $ShimPath)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.ArgumentList.Add("-NoProfile")
    $psi.ArgumentList.Add("-Command")
    $psi.ArgumentList.Add($Command)
    $psi.Environment["SHELLFIX_WSL_DISTRO"] = $WslDistro
    if ($DebugShim) {
        $psi.Environment["PWSH_SHIM_DEBUG"] = "1"
    }

    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()

    [pscustomobject]@{
        ExitCode = $proc.ExitCode
        Stdout = $stdout
        Stderr = $stderr
    }
}

function Test-Smoke {
    param(
        [string]$Name,
        [string]$Command,
        [string]$Expect,
        [switch]$DebugShim
    )

    $result = Invoke-ShimCommand -Command $Command -DebugShim:$DebugShim
    if ($result.ExitCode -eq 0 -and (($result.Stdout + $result.Stderr) -match $Expect)) {
        Write-Host "PASS: $Name" -ForegroundColor Green
        $script:pass++
        return
    }

    Write-Host "FAIL: $Name" -ForegroundColor Red
    Write-Host "  Command: $Command"
    Write-Host "  Exit: $($result.ExitCode)"
    Write-Host "  Expected: /$Expect/"
    Write-Host "  Stdout: $($result.Stdout.Trim())"
    Write-Host "  Stderr: $($result.Stderr.Trim())"
    $script:fail++
}

function Test-WslAvailable {
    try {
        $out = & wsl.exe -d $WslDistro -e echo ok 2>$null
        return ($LASTEXITCODE -eq 0 -and $out -match "ok")
    } catch {
        return $false
    }
}

Write-Host "============================================="
Write-Host "  shellfix CI Smoke Tests"
Write-Host "  Shim: $ShimPath"
if (Test-Path $ShimPath) {
    $shimItem = Get-Item $ShimPath
    $shimHash = (Get-FileHash $ShimPath -Algorithm SHA256).Hash.Substring(0, 12)
    Write-Host "  Built: $($shimItem.LastWriteTime)  SHA256: $shimHash..."
} else {
    Write-Host "FATAL: Shim not found: $ShimPath" -ForegroundColor Red
    exit 1
}
Write-Host "  WSL distro: $WslDistro"
Write-Host "============================================="

Test-Smoke "PowerShell passthrough" 'Write-Output "ps-ok"' 'ps-ok'
Test-Smoke "Native tool direct policy" 'git --version' '\[SHIM\] Native direct: .*git' -DebugShim
Test-Smoke "Native python inline" 'python -c "import sys; print(sys.executable)"' '^[A-Za-z]:\\'
$nativePythonRegex = @'
python -c "import re
for m in re.finditer(r'https?://[^\s\'\",)]+', 'https://example.com/path'):
    print(m.group())
"
'@.Trim()
Test-Smoke "Native python multiline regex" $nativePythonRegex 'https://example.com/path'

if (Test-WslAvailable) {
    Test-Smoke "Explicit WSL command" "wsl -d $WslDistro -- echo wsl-ok" 'wsl-ok'
    Test-Smoke "WSL bash && chain" "wsl -d $WslDistro -- bash -c `"echo left && echo right`"" 'left[\s\S]*right'
    Test-Smoke "Python slice syntax" ('wsl -d {0} -- bash -c "python3 -c ''print(list(range(5))[1:-1])''"' -f $WslDistro) '\[1.*2.*3\]'
} else {
    Write-Host "SKIP: WSL smoke tests (distro unavailable: $WslDistro)" -ForegroundColor Yellow
    $skip += 3
}

Write-Host "============================================="
Write-Host "  PASS: $pass"
if ($skip -gt 0) { Write-Host "  SKIP: $skip" -ForegroundColor Yellow }
if ($fail -gt 0) { Write-Host "  FAIL: $fail" -ForegroundColor Red }
Write-Host "============================================="

exit $fail
