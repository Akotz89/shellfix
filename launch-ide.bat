@echo off
REM === shellfix IDE Launcher ===
REM Prepends the shellfix shim directory to PATH so the powershell.exe shim
REM wins the PATH race against System32\WindowsPowerShell\v1.0\.
REM This makes the agent's run_command go through shellfix automatically.
REM
REM Usage: launch-ide.bat "C:\path\to\IDE.exe" [optional args]
REM
REM Safe: only affects this process tree (IDE + children).
REM Reversible: launch the IDE directly to bypass.

if "%~1"=="" (
    echo Usage: launch-ide.bat "C:\path\to\IDE.exe" [args...]
    exit /b 1
)

set "PATH=%~dp0;%PATH%"
start "" %*
