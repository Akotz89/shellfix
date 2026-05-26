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

        $combinedOutput = "$stdout`n$stderr"
        if ($combinedOutput -match $ExpectPattern) {
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
            $snippet = ($combinedOutput -split "`n" | Where-Object { $_.Trim() -ne "" } | Select-Object -First 5) -join " | "
            if ($snippet.Length -gt 120) { $snippet = $snippet.Substring(0, 120) + "..." }
            Write-Host "        Got: $snippet" -ForegroundColor DarkGray
            $script:fail++
        }
    } finally {
        Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
    }
}

function Run-ProxyTestIf {
    param(
        [bool]$Condition,
        [string]$Name,
        [string]$Command,
        [string]$ExpectPattern,
        [string]$SkipReason
    )

    if ($Condition) {
        Run-ProxyTest $Name $Command $ExpectPattern
    } else {
        Write-Host "  SKIP: $Name ($SkipReason)" -ForegroundColor Yellow
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
Run-ProxyTest "PS variable" '$PSVersionTable.PSVersion.Major -in 5,7' 'True'
Run-ProxyTest "PS semicolon" 'echo "one"; echo "two"; echo "three"' 'three'

# --- Native inline developer tools ---
Write-Host "`nNative inline tools:" -ForegroundColor Cyan
Run-ProxyTest "python -c native path" 'python -c "import sys; print(sys.executable)"' '[A-Za-z]:\\'
$nativePythonRegex = @'
python -c "import re
for m in re.finditer(r'https?://[^\s\'\",)]+', 'https://example.com/path'):
    print(m.group())
"
'@.Trim()
Run-ProxyTest "python -c multiline regex" $nativePythonRegex 'https://example.com/path'
Run-ProxyTest "node -e native path" 'node -e "console.log(process.execPath)"' '[A-Za-z]:\\'

# --- WSL safe ---
Write-Host "`nWSL safe (no rewrite):" -ForegroundColor Cyan
Run-ProxyTest "wsl echo" "wsl -d $WslDistro -- echo `"hello wsl`"" 'hello wsl'
Run-ProxyTest "wsl uname" "wsl -d $WslDistro -- uname -s" 'Linux'
$wslPythonHeredoc = @'
wsl -d __DISTRO__ -- python3 << 'PYEOF'
import json
print('HEREDOC_OK', sorted({'b': 2, 'a': 1}.keys()))
PYEOF
'@.Trim().Replace('__DISTRO__', $WslDistro)
Run-ProxyTest "wsl python heredoc stdin" $wslPythonHeredoc 'HEREDOC_OK'
$wslNodeHeredoc = @'
wsl -d __DISTRO__ -- node << 'NODE'
console.log('NODE_HEREDOC_OK')
NODE
'@.Trim().Replace('__DISTRO__', $WslDistro)
Run-ProxyTest "wsl node heredoc stdin" $wslNodeHeredoc 'NODE_HEREDOC_OK'
$wslBashNestedHeredoc = @'
wsl -d __DISTRO__ -- bash -c "
cat > /tmp/shellfix_shape_proxy.drawio << 'ENDXML'
<mxCell id=\"10\" value=\"2D FW\" style=\"shape=mxgraph.networks.2d.firewall;sketch=0;html=1;fillColor=#C62828;strokeColor=none;verticalLabelPosition=bottom;verticalAlign=top;align=center;fontSize=11;fontStyle=1;\" vertex=\"1\" parent=\"1\">
  <mxGeometry x=\"320\" y=\"150\" width=\"60\" height=\"48\" as=\"geometry\" />
</mxCell>
ENDXML
grep fontStyle /tmp/shellfix_shape_proxy.drawio
"
'@.Trim().Replace('__DISTRO__', $WslDistro)
Run-ProxyTest "wsl bash nested heredoc with trailing command" $wslBashNestedHeredoc 'fontStyle=1'
$wslBashStdinHeredoc = @'
wsl -d __DISTRO__ -- bash -s <<'BASH'
cat > /tmp/shellfix_bash_stdin.txt <<'EOF'
bash stdin ok
EOF
cat /tmp/shellfix_bash_stdin.txt
BASH
'@.Trim().Replace('__DISTRO__', $WslDistro)
Run-ProxyTest "wsl bash -s heredoc stdin" $wslBashStdinHeredoc 'bash stdin ok'
$wslMultipleHeredocs = @'
wsl -d __DISTRO__ -- bash -c "
cat > /tmp/shellfix_multi_a.txt <<'EOF1'
one
EOF1
cat > /tmp/shellfix_multi_b.txt <<-'EOF2'
	two
EOF2
cat /tmp/shellfix_multi_a.txt /tmp/shellfix_multi_b.txt
"
'@.Trim().Replace('__DISTRO__', $WslDistro)
Run-ProxyTest "wsl bash multiple heredocs" $wslMultipleHeredocs 'one[\s\S]*two'
Run-ProxyTest "cmd wrapper wsl" "cmd /c `"wsl -d $WslDistro -- bash -c \`"echo cmd-wsl-ok\`"`"" 'cmd-wsl-ok'
Run-ProxyTest "cmd wrapper npx" "cmd /c npx -y npm@latest --version" '\d+\.\d+\.\d+'

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
Run-ProxyTest "combined && plus python slice" "wsl -d $WslDistro -- bash -c ""echo ok && python3 -c 'print([1,2,3,4,5][1:-1])'""" 'ok[\s\S]*\[2.*3.*4\]'

$wslMultilinePython = @'
wsl -d __DISTRO__ -- bash -c "cd /tmp && python3 -c \"
import re
print('PATH token: $PATH:/usr/local/bin')
for m in re.finditer(r'https?://[^\\s,)]+', 'https://example.com/path, next'):
    print(m.group())
def emit(a, b):
    print('Converted SVG -> PNG at %sx%s' % (a, b))
emit(3000, 2250)
\""
'@.Trim().Replace('__DISTRO__', $WslDistro)
Run-ProxyTest "wsl multiline python -c with commas" $wslMultilinePython 'Converted SVG -> PNG at 3000x2250'
$wslVenvMultilinePython = @'
wsl -d __DISTRO__ -- bash -c "/home/aaron/.venvs/diagrams/bin/python -c \"
class O: pass
o = O()
print(type(type(o).__dict__.get('container', None)))
\""
'@.Trim().Replace('__DISTRO__', $WslDistro)
Run-ProxyTest "wsl venv multiline python -c with property lookup" $wslVenvMultilinePython "<class 'NoneType'>"
Run-ProxyTest "wsl bash PATH token" "wsl -d $WslDistro -- bash -c `"echo `$PATH:/usr/local/bin`"" '/usr/local/bin'
Run-ProxyTest "wsl bash escaped HOME export" "wsl -d $WslDistro -- bash -c `"export PATH=\`$HOME/.local/bin:\`$PATH && echo HOME=\`$HOME`"" 'HOME=/home/'
Run-ProxyTest "wsl bash literal HOME path lookup" "wsl -d $WslDistro -- bash -c `"test -d `$HOME && echo HOME_OK=`$HOME`"" 'HOME_OK=/home/'

# --- Issue #3: Nested quotes ---
Write-Host "`nIssue #3 (nested quotes):" -ForegroundColor Cyan
Run-ProxyTest "single in double" "wsl -d $WslDistro -- bash -c ""echo 'hello world'""" 'hello world'
Run-ProxyTest "python in bash" "wsl -d $WslDistro -- bash -c ""python3 -c 'print(1+2)'""" '3'

# --- Full-path native executable calls ---
Write-Host "`nFull-path native direct:" -ForegroundColor Cyan
$d2Path = (where.exe d2 2>$null | Select-Object -First 1)
if (-not $d2Path -and (Test-Path 'C:\Program Files\D2\d2.exe')) {
    $d2Path = 'C:\Program Files\D2\d2.exe'
}
$d2Input = Join-Path ([System.IO.Path]::GetTempPath()) "shellfix_proxy_d2_$([guid]::NewGuid().ToString('N')).d2"
$d2Output = [System.IO.Path]::ChangeExtension($d2Input, ".png")
try {
    if ($d2Path) {
        Set-Content -LiteralPath $d2Input -Value "x -> y" -Encoding UTF8
        Run-ProxyTest "full-path d2 with stderr redirect" "& `"$d2Path`" `"$d2Input`" `"$d2Output`" 2>&1" 'success: successfully compiled'
    } else {
        Run-ProxyTestIf $false "full-path d2 with stderr redirect" "" "" "d2 not found"
    }
} finally {
    Remove-Item -LiteralPath $d2Input, $d2Output -ErrorAction SilentlyContinue
}

$dotPath = (where.exe dot 2>$null | Select-Object -First 1)
if (-not $dotPath -and (Test-Path 'C:\Program Files\Graphviz\bin\dot.exe')) {
    $dotPath = 'C:\Program Files\Graphviz\bin\dot.exe'
}
if ($dotPath) {
    Run-ProxyTest "full-path Graphviz dot with stderr redirect" "& `"$dotPath`" -V 2>&1" 'graphviz version'
} else {
    Run-ProxyTestIf $false "full-path Graphviz dot with stderr redirect" "" "" "dot not found"
}

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
