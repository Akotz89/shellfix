<#
.SYNOPSIS
  Replay the EXACT shell failures from this chat session through the shellfix
  proxy to verify they are fixed.
#>
param(
    [string]$ShimPath,
    [string]$WslDistro = "Ubuntu-24.04"
)

$ErrorActionPreference = 'SilentlyContinue'
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
$shim = $ShimPath

if (-not $shim -or -not (Test-Path $shim)) { Write-Error "Shim not found. Build first or pass -ShimPath."; exit 1 }

$pass = 0; $fail = 0; $total = 0

function Test-Proxy {
    param([string]$Name, [string]$Command, [string]$Expect)
    $script:total++
    
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $shim
    $psi.Arguments = "-NoLogo -NoProfile"
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.EnvironmentVariables["PWSH_SHIM_BYPASS"] = ""

    $proc = [System.Diagnostics.Process]::Start($psi)
    # Force UTF-8 without BOM on stdin to prevent ﻿ character
    $proc.StandardInput.BaseStream.Flush()
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $sw = New-Object System.IO.StreamWriter($proc.StandardInput.BaseStream, $utf8NoBom)
    $sw.AutoFlush = $true
    Start-Sleep -Milliseconds 2500

    $sw.WriteLine($Command)
    $sw.WriteLine("exit")
    $sw.Close()

    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    [void]$proc.WaitForExit(15000)

    $combinedOutput = "$stdout`n$stderr"
    if ($combinedOutput -match [regex]::Escape($Expect)) {
        Write-Host "  PASS: $Name" -ForegroundColor Green
        $script:pass++
    } else {
        Write-Host "  FAIL: $Name" -ForegroundColor Red
        Write-Host "    Expected to contain: $Expect"
        $preview = ($combinedOutput -split "`n" | Select-Object -First 8) -join "`n"
        Write-Host "    Output: $preview"
        $script:fail++
    }
    if (-not $proc.HasExited) { $proc.Kill() }
}

Write-Host "============================================="
Write-Host "  Replay: Actual Session Failures via Shim"
Write-Host "  Shim: $shim"
$shimItem = Get-Item $shim
$shimHash = (Get-FileHash $shim -Algorithm SHA256).Hash.Substring(0, 12)
Write-Host "  Built: $($shimItem.LastWriteTime)  SHA256: $shimHash..."
Write-Host "  WSL distro: $WslDistro"
Write-Host "============================================="
Write-Host ""

# --- Pattern A: PS parsing Python 'for' as PS keyword (step 3011) ---
Write-Host "Pattern A: PS parsing Python 'for' as PS keyword (step 3011):"
Test-Proxy "python for-loop in wsl bash" `
    "wsl -d $WslDistro -- bash -c `"python3 -c ''for i in range(3): print(i)''`"" `
    "2"

# --- Pattern A2: native multiline python -c URL regex (Antigravity fallback) ---
Write-Host ""
Write-Host "Pattern A2: native multiline python -c URL regex:"
$nativePythonRegex = @'
python -c "import re
for m in re.finditer(r'https?://[^\s\'\",)]+', 'https://example.com/path'):
    print(m.group())
"
'@.Trim()
Test-Proxy "native python multiline regex" $nativePythonRegex "https://example.com/path"

# --- Pattern A3: explicit WSL multiline python -c from Antigravity SVG conversion ---
Write-Host ""
Write-Host "Pattern A3: WSL multiline python -c SVG conversion shape:"
$svgInline = @'
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
Test-Proxy "wsl multiline cairosvg-style payload" $svgInline "Converted SVG -> PNG at 3000x2250"

# --- Pattern A4: Bash variable token that PowerShell used to parse as $PATH: ---
Write-Host ""
Write-Host 'Pattern A4: WSL $PATH:/usr/local/bin token:'
Test-Proxy "wsl PATH colon token" `
    "wsl -d $WslDistro -- bash -c `"echo `$PATH:/usr/local/bin`"" `
    "/usr/local/bin"

# --- Pattern A: PS parsing [print()] as array index (step 3454) ---
Write-Host ""
Write-Host "Pattern A: PS parsing [print()] as array index (step 3454):"
Test-Proxy "python list comp with brackets" `
    "wsl -d $WslDistro -- bash -c `"echo hello | python3 -c ''import sys; [print(line.strip()) for line in sys.stdin]''`"" `
    "hello"

# --- Pattern B: sed with complex expression (step 3356) ---
Write-Host ""
Write-Host "Pattern B: sed with newlines in wsl (step 3356):"
Test-Proxy "sed insert + && chain" `
    "wsl -d $WslDistro -- bash -c `"echo original > /tmp/sed_replay.txt && sed -i ''1i\inserted'' /tmp/sed_replay.txt && cat /tmp/sed_replay.txt`"" `
    "inserted"

# --- Pattern B: python3 with open() nested parens (steps 3462/3480/3567) ---
Write-Host ""
Write-Host "Pattern B: python3 open() nested parens (step 3462):"
Test-Proxy "python open() in bash -c" `
    "wsl -d $WslDistro -- bash -c `"echo ''{\`"key\`":\`"value\`"}'' > /tmp/replay_json.json && python3 -c ''import json; d=json.load(open(\`"/tmp/replay_json.json\`")); print(d[\`"key\`"])''`"" `
    "value"

# --- Pattern B: python f-string with nested quotes (step 3454) ---
Write-Host ""
Write-Host "Pattern B: python nested call (step 3454):"
Test-Proxy "python nested call" `
    ('wsl -d {0} -- bash -c "python3 -c ''print(chr(110)+chr(97)+chr(109)+chr(101)+chr(61)+chr(116)+chr(101)+chr(115)+chr(116))''"' -f $WslDistro) `
    "name=test"

# --- Pattern C: multi-line heredoc (step 3342) ---
Write-Host ""
Write-Host "Pattern C: YAML write through bash (step 3342):"
Test-Proxy "yaml write" `
    ('wsl -d {0} -- bash -c "printf ''service:\n  image: test\n  ports:\n    - 8080:8080\n'' > /tmp/replay_yaml.txt && cat /tmp/replay_yaml.txt"' -f $WslDistro) `
    "service"

# --- Issue #1: && chain ---
Write-Host ""
Write-Host "Issue #1: && chain (original bug):"
Test-Proxy "triple && chain" `
    "wsl -d $WslDistro -- bash -c `"echo step1 && echo step2 && echo step3`"" `
    "step3"

# --- Issue #2: [1:-1] slice ---
Write-Host ""
Write-Host "Issue #2: [1:-1] slice (original bug):"
Test-Proxy "python slice syntax" `
    ('wsl -d {0} -- bash -c "python3 -c ''print(list(range(5))[1:-1])''"' -f $WslDistro) `
    "1, 2, 3"

# --- Issue #3: nested single quotes ---
Write-Host ""
Write-Host "Issue #3: nested quotes (original bug):"
Test-Proxy "nested single in double" `
    "wsl -d $WslDistro -- bash -c `"python3 -c ''print(1+2)''`"" `
    "3"

# --- Combined stress test ---
Write-Host ""
Write-Host "Combined: && + [1:-1] + open() + f-string:"
Test-Proxy "combined stress test" `
    ('wsl -d {0} -- bash -c "echo ok && python3 -c ''print([1,2,3,4,5][1:-1])''"' -f $WslDistro) `
    "2, 3, 4"

# --- Pattern D: D2 full-path stderr false positive ---
Write-Host ""
Write-Host "Pattern D: full-path native executable with 2>&1:"
$d2Path = (where.exe d2 2>$null | Select-Object -First 1)
if (-not $d2Path -and (Test-Path 'C:\Program Files\D2\d2.exe')) {
    $d2Path = 'C:\Program Files\D2\d2.exe'
}
if ($d2Path) {
    $d2Input = Join-Path ([System.IO.Path]::GetTempPath()) "shellfix_replay_d2_$([guid]::NewGuid().ToString('N')).d2"
    $d2Output = [System.IO.Path]::ChangeExtension($d2Input, ".png")
    try {
        Set-Content -LiteralPath $d2Input -Value "x -> y" -Encoding UTF8
        Test-Proxy "full-path d2 stderr redirect" "& `"$d2Path`" `"$d2Input`" `"$d2Output`" 2>&1" "success: successfully compiled"
    } finally {
        Remove-Item -LiteralPath $d2Input, $d2Output -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "  SKIP: full-path d2 stderr redirect (d2 not found)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "============================================="
Write-Host "  Results: $pass / $total PASS, $fail FAIL"
Write-Host "============================================="

if ($null -eq $originalShellfixWslDistro) {
    Remove-Item Env:SHELLFIX_WSL_DISTRO -ErrorAction SilentlyContinue
} else {
    $env:SHELLFIX_WSL_DISTRO = $originalShellfixWslDistro
}

exit $fail
