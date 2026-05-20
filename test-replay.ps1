<#
.SYNOPSIS
  Replay the EXACT shell failures from this chat session through the shellfix
  proxy to verify they are fixed.
#>
$ErrorActionPreference = 'SilentlyContinue'
$shim = "C:\Users\Aaron\bin\powershell.exe"

if (-not (Test-Path $shim)) { Write-Error "Shim not found: $shim"; exit 1 }

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
    $proc.WaitForExit(15000)

    if ($stdout -match [regex]::Escape($Expect)) {
        Write-Host "  PASS: $Name" -ForegroundColor Green
        $script:pass++
    } else {
        Write-Host "  FAIL: $Name" -ForegroundColor Red
        Write-Host "    Expected to contain: $Expect"
        $preview = ($stdout -split "`n" | Select-Object -First 5) -join "`n"
        Write-Host "    Stdout: $preview"
        if ($stderr) {
            $errPreview = ($stderr -split "`n" | Select-Object -First 3) -join "`n"
            Write-Host "    Stderr: $errPreview"
        }
        $script:fail++
    }
    if (-not $proc.HasExited) { $proc.Kill() }
}

Write-Host "============================================="
Write-Host "  Replay: Actual Session Failures via Shim"
Write-Host "  Shim: $shim"
Write-Host "============================================="
Write-Host ""

# --- Pattern A: PS parsing Python 'for' as PS keyword (step 3011) ---
Write-Host "Pattern A: PS parsing Python 'for' as PS keyword (step 3011):"
Test-Proxy "python for-loop in wsl bash" `
    'wsl -d Ubuntu-24.04 -- bash -c "python3 -c ''for i in range(3): print(i)''"' `
    "2"

# --- Pattern A: PS parsing [print()] as array index (step 3454) ---
Write-Host ""
Write-Host "Pattern A: PS parsing [print()] as array index (step 3454):"
Test-Proxy "python list comp with brackets" `
    'wsl -d Ubuntu-24.04 -- bash -c "echo hello | python3 -c ''import sys; [print(line.strip()) for line in sys.stdin]''"' `
    "hello"

# --- Pattern B: sed with complex expression (step 3356) ---
Write-Host ""
Write-Host "Pattern B: sed with newlines in wsl (step 3356):"
Test-Proxy "sed insert + && chain" `
    'wsl -d Ubuntu-24.04 -- bash -c "echo original > /tmp/sed_replay.txt && sed -i ''1i\inserted'' /tmp/sed_replay.txt && cat /tmp/sed_replay.txt"' `
    "inserted"

# --- Pattern B: python3 with open() nested parens (steps 3462/3480/3567) ---
Write-Host ""
Write-Host "Pattern B: python3 open() nested parens (step 3462):"
Test-Proxy "python open() in bash -c" `
    'wsl -d Ubuntu-24.04 -- bash -c "echo ''{\"key\":\"value\"}'' > /tmp/replay_json.json && python3 -c ''import json; d=json.load(open(\"/tmp/replay_json.json\")); print(d[\"key\"])''"' `
    "value"

# --- Pattern B: python f-string with nested quotes (step 3454) ---
Write-Host ""
Write-Host "Pattern B: python f-string (step 3454):"
Test-Proxy "python f-string with dict access" `
    'wsl -d Ubuntu-24.04 -- bash -c "python3 -c ''d={\"name\":\"test\",\"val\":42}; print(f\"name={d[chr(110)+chr(97)+chr(109)+chr(101)]}\")''"' `
    "name=test"

# --- Pattern C: multi-line heredoc (step 3342) ---
Write-Host ""
Write-Host "Pattern C: heredoc multi-line YAML (step 3342):"
Test-Proxy "heredoc yaml write" `
    "wsl -d Ubuntu-24.04 -- bash -c ""cat > /tmp/replay_yaml.txt << 'EOF'
service:
  image: test
  ports:
    - 8080:8080
EOF
cat /tmp/replay_yaml.txt""" `
    "service"

# --- Issue #1: && chain ---
Write-Host ""
Write-Host "Issue #1: && chain (original bug):"
Test-Proxy "triple && chain" `
    'wsl -d Ubuntu-24.04 -- bash -c "echo step1 && echo step2 && echo step3"' `
    "step3"

# --- Issue #2: [1:-1] slice ---
Write-Host ""
Write-Host "Issue #2: [1:-1] slice (original bug):"
Test-Proxy "python slice syntax" `
    'wsl -d Ubuntu-24.04 -- bash -c "python3 -c ''print(list(range(5))[1:-1])''"' `
    "1, 2, 3"

# --- Issue #3: nested single quotes ---
Write-Host ""
Write-Host "Issue #3: nested quotes (original bug):"
Test-Proxy "nested single in double" `
    'wsl -d Ubuntu-24.04 -- bash -c "python3 -c ''print(1+2)''"' `
    "3"

# --- Combined stress test ---
Write-Host ""
Write-Host "Combined: && + [1:-1] + open() + f-string:"
Test-Proxy "combined stress test" `
    'wsl -d Ubuntu-24.04 -- bash -c "echo ''[1,2,3,4,5]'' > /tmp/stress.json && python3 -c ''import json; d=json.load(open(\"/tmp/stress.json\")); print(d[1:-1])''"' `
    "2, 3, 4"

Write-Host ""
Write-Host "============================================="
Write-Host "  Results: $pass / $total PASS, $fail FAIL"
Write-Host "============================================="
