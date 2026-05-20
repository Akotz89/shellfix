@echo off
REM === Antigravity IDE Launcher with Shellfix PATH ===
REM Prepends C:\Users\Aaron\bin to PATH so the powershell.exe shim
REM wins the PATH race against System32\WindowsPowerShell\v1.0\.
REM This makes run_command go through the shellfix proxy automatically.
REM
REM Safe: only affects this process tree (IDE + children).
REM Reversible: launch the IDE directly to bypass.

set "PATH=C:\Users\Aaron\bin;%PATH%"
start "" "C:\Users\Aaron\AppData\Local\Programs\Antigravity IDE\Antigravity IDE.exe" %*
