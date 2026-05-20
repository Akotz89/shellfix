' === Antigravity IDE Launcher (no console flash) ===
' Runs launch-antigravity.bat silently — use this for taskbar/Start pins.
Set WshShell = CreateObject("WScript.Shell")
WshShell.Run Chr(34) & CreateObject("Scripting.FileSystemObject").GetParentFolderName(WScript.ScriptFullName) & "\launch-antigravity.bat" & Chr(34), 0, False
