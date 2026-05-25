using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// shellfix — C# Shim (Layer 1)
/// 
/// Two modes of operation:
///   1. ONE-SHOT: Intercepts `powershell -Command "..."` calls and routes:
///      - Bash commands → WSL bash -c (with escaping)
///      - Complex PS commands → temp .ps1 file + powershell -File
///      - Simple PS commands → real powershell.exe passthrough
///   2. SESSION PROXY: When launched as an interactive shell (no -Command),
///      spawns real powershell.exe and proxies stdin/stdout. Each line of
///      stdin is inspected and rewritten to fix PS 5.1 parse errors in
///      WSL/bash commands (&&, [N:-N], nested quotes).
///
/// Install: compile to powershell.exe and place in a PATH directory that
/// precedes C:\Windows\System32\WindowsPowerShell\v1.0\, or configure
/// your IDE to use this binary as its terminal shell.
///
/// Kill switch: set environment variable PWSH_SHIM_BYPASS=1 to disable.
/// Debug mode: set PWSH_SHIM_DEBUG=1 to log decisions to stderr.
/// </summary>

const string Pwsh7Path = @"C:\Program Files\PowerShell\7\pwsh.exe";
const string Pwsh5Path = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
const string WslExe = @"C:\Windows\System32\wsl.exe";
const string DefaultWslDistro = "Ubuntu-24.04";

// --- Choose PowerShell backend: prefer pwsh 7, fall back to 5.1 ---
// pwsh 7 natively fixes: &&/|| operators, NativeCommandError, UTF-8,
// Set-Content encoding, and most argument escaping edge cases.
// This eliminates ~60% of the problems ShellFix was built to work around.
// Store in env var so static methods can read it.
// Override: set SHELLFIX_FORCE_PS5=1 to revert to PS 5.1 if pwsh 7 causes issues.
bool forcePs5 = Environment.GetEnvironmentVariable("SHELLFIX_FORCE_PS5") == "1";
string RealPowerShell = (!forcePs5 && File.Exists(Pwsh7Path)) ? Pwsh7Path : Pwsh5Path;
Environment.SetEnvironmentVariable("SHELLFIX_PS_BACKEND", RealPowerShell);
bool UsingPwsh7 = RealPowerShell == Pwsh7Path;

// --- Kill switch ---
if (Environment.GetEnvironmentVariable("PWSH_SHIM_BYPASS") == "1")
{
    return RunProcess(RealPowerShell, args);
}

bool debug = Environment.GetEnvironmentVariable("PWSH_SHIM_DEBUG") == "1";

// --- Breadcrumb: signal to the profile that the shim is active ---
Environment.SetEnvironmentVariable("SHELLFIX_ACTIVE", "1");

// --- Command logging: build empirical data on what agents send ---
// Log file: %TEMP%\shellfix_commands.log
// Each line: timestamp | classification | first 200 chars of command
// Opt-in via SHELLFIX_LOG=1 or always-on in debug mode
bool logging = debug || Environment.GetEnvironmentVariable("SHELLFIX_LOG") == "1";
string? logPath = logging ? Path.Combine(Path.GetTempPath(), "shellfix_commands.log") : null;

// --- Extract the command string from RAW command line ---
// CRITICAL: We must NOT rely on args[] because PowerShell has already
// tokenized them. Tokens like &&, [1:-1], and nested single quotes
// cause PS parser errors BEFORE our Main() runs with parsed args.
// Instead, read the raw command line and extract -Command payload.
string? commandStr = null;
bool foundCommand = false;

// Prefer argv when the launcher passed -Command as a real argument. This
// preserves nested escaped quotes in WSL/Python payloads better than reparsing
// Environment.CommandLine. Fall back to the raw line for non-standard launchers.
for (int i = 0; i < args.Length; i++)
{
    if (args[i].Equals("-Command", StringComparison.OrdinalIgnoreCase))
    {
        foundCommand = true;
        commandStr = string.Join(" ", args.Skip(i + 1));
        break;
    }
}

string rawCmdLine = Environment.CommandLine;
if (debug) Console.Error.WriteLine($"[SHIM] Raw cmdline: {rawCmdLine.Substring(0, Math.Min(200, rawCmdLine.Length))}...");

if (!foundCommand)
{
    int cmdIdx = rawCmdLine.IndexOf("-Command", StringComparison.OrdinalIgnoreCase);
    if (cmdIdx >= 0)
    {
        foundCommand = true;
        commandStr = rawCmdLine.Substring(cmdIdx + "-Command".Length).Trim();
        // Strip outer quotes if the IDE wrapped the whole command in quotes
        if (commandStr.Length >= 2 && commandStr[0] == '"' && commandStr[commandStr.Length - 1] == '"')
        {
            commandStr = commandStr.Substring(1, commandStr.Length - 2);
        }
        // Unescape \" → " for launchers that only expose the raw command line.
        commandStr = commandStr.Replace("\\\"", "\"");
    }
}

// If no -Command flag, decide between interactive proxy and passthrough
if (!foundCommand || string.IsNullOrWhiteSpace(commandStr))
{
    // Check if stdin is redirected (IDE terminal mode) or if launched
    // with args that indicate interactive use (no args, or just flags
    // like -NoExit, -NoProfile, -NoLogo)
    bool isInteractive = !Console.IsInputRedirected || args.Length == 0 ||
        args.All(a => a.StartsWith("-", StringComparison.OrdinalIgnoreCase) &&
                      !a.Equals("-Command", StringComparison.OrdinalIgnoreCase) &&
                      !a.Equals("-File", StringComparison.OrdinalIgnoreCase) &&
                      !a.Equals("-EncodedCommand", StringComparison.OrdinalIgnoreCase));
    
    if (isInteractive)
    {
        if (debug) Console.Error.WriteLine("[SHIM] Interactive mode — starting session proxy");
        return RunInteractiveProxy(args, debug);
    }
    
    if (debug) Console.Error.WriteLine($"[SHIM] No -Command found, passthrough: {string.Join(" ", args)}");
    return RunProcess(RealPowerShell, args);
}

if (TryRunNativeInlineCommand(commandStr, debug, out int nativeInlineExitCode))
{
    return nativeInlineExitCode;
}

if (TryRunNativeDirectCommand(commandStr, debug, out int nativeDirectExitCode))
{
    return nativeDirectExitCode;
}

// --- Classify: bash or PowerShell? ---
bool isBash = LooksLikeBash(commandStr);
string classification = isBash ? "BASH" : "PS";

if (debug) Console.Error.WriteLine($"[SHIM] Backend={Path.GetFileName(RealPowerShell)} Classified={classification}: {commandStr.Substring(0, Math.Min(80, commandStr.Length))}...");

// --- Log command for analysis ---
if (logging && logPath != null)
{
    try
    {
        string snippet = commandStr.Length > 200 ? commandStr.Substring(0, 200) + "..." : commandStr;
        snippet = snippet.Replace('\n', ' ').Replace('\r', ' ');
        string logLine = $"{DateTime.Now:o} | {classification} | {Path.GetFileName(RealPowerShell)} | {snippet}\n";
        File.AppendAllText(logPath, logLine);
    }
    catch { /* logging must never break the shim */ }
}

if (isBash)
{
    // If command is already wrapped in wsl -d ... --, pass through directly
    // without re-wrapping in another bash -c layer
    if (IsAlreadyWslWrapped(commandStr))
    {
        if (debug) Console.Error.WriteLine("[SHIM] Already WSL-wrapped, direct passthrough");
        return RunWslPassthrough(commandStr, debug);
    }
    // Route to WSL bash
    return RunWslBash(commandStr, debug);
}
else
{
    // ALL PS commands route through -File mode unconditionally.
    // This eliminates the entire class of quoting/escaping failures:
    // -File reads script content as-is with no quote interpretation.
    // HasDangerousQuoting is kept for debug logging but not gating.
    if (debug)
    {
        bool dangerous = HasDangerousQuoting(commandStr);
        Console.Error.WriteLine($"[SHIM] PS via -File (dangerous={dangerous})");
    }
    return RunPsViaFile(commandStr, debug);
}

// ============================================================
// Heuristic classifier
// ============================================================
static bool LooksLikeBash(string cmd)
{
    cmd = cmd.Trim();
    if (string.IsNullOrEmpty(cmd)) return false;

    // --- HIGHEST PRIORITY: already-wrapped WSL commands ---
    // If the command starts with wsl/wsl.exe, it's bash-bound by definition.
    // This catches: wsl -d Ubuntu-24.04 -- bash -c "cmd1 && cmd2"
    if (cmd.StartsWith("wsl ", StringComparison.OrdinalIgnoreCase) ||
        cmd.StartsWith("wsl.exe ", StringComparison.OrdinalIgnoreCase))
        return true;

    // Extract the first word/token
    string firstWord = cmd.Split(new[] { ' ', '\t', '\r', '\n' }, 2, StringSplitOptions.RemoveEmptyEntries)[0];

    // Native developer tools stay native when installed on Windows. Explicit
    // wsl commands and Unix-only commands still route to WSL.
    if (IsNativeFirstCommand(firstWord) && ResolveNativeCommand(firstWord) is not null)
        return false;

    // --- Strong PowerShell indicators → NOT bash ---
    // Starts with $ (variable), [ (type), @{ (hashtable), & (call operator)
    if (cmd[0] == '$' || cmd[0] == '[' || cmd.StartsWith("@{") || cmd.StartsWith("& "))
        return false;

    // PowerShell Verb-Noun cmdlets
    string[] psVerbs = { "Get-", "Set-", "New-", "Remove-", "Test-", "Write-",
                         "Select-", "Where-", "ForEach-", "Sort-", "Group-",
                         "Invoke-", "Start-", "Stop-", "Import-", "Export-",
                         "Add-", "Clear-", "Copy-", "Move-", "Out-",
                         "ConvertTo-", "ConvertFrom-", "Measure-", "Compare-",
                         "Register-", "Unregister-", "Enable-", "Disable-",
                         "Format-", "Resolve-", "Split-", "Join-", "Update-" };
    foreach (var verb in psVerbs)
    {
        if (firstWord.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
            return false;
    }

    // PS-specific syntax
    if (Regex.IsMatch(cmd, @"^\s*if\s*\(")) return false;         // if ($x)
    if (Regex.IsMatch(cmd, @"^\s*try\s*\{")) return false;        // try {
    if (Regex.IsMatch(cmd, @"^\s*param\s*\(")) return false;      // param(
    if (Regex.IsMatch(cmd, @"^\s*function\s+\w")) return false;   // function foo
    if (Regex.IsMatch(cmd, @"^\s*\.\s+")) return false;           // . .\script.ps1

    // --- Strong bash indicators → IS bash ---
    string[] bashCommands = {
        // core unix tools
        "grep", "sed", "awk", "find", "head", "tail", "wc", "sort", "uniq",
        "cut", "tr", "xargs", "tee", "cat", "less", "more",
        // file ops
        "chmod", "chown", "ln", "readlink", "touch", "stat", "file",
        "realpath", "basename", "dirname", "md5sum", "sha256sum",
        // diffs
        "diff", "patch",
        // system
        "uname", "whoami", "hostname", "id", "env", "printenv", "export",
        "ps", "kill", "top", "htop", "df", "du", "mount", "umount",
        "systemctl", "journalctl", "service",
        // network
        "curl", "wget", "ssh", "scp", "rsync", "ping", "netstat", "ss",
        "nc", "nmap", "dig", "nslookup",
        // archives
        "tar", "gzip", "gunzip", "zip", "unzip", "bzip2",
        // text/misc
        "echo", "printf", "date", "cal", "seq", "yes",
        "xxd", "od", "strings", "bc",
        // dev tools — only tools that are generally WSL/Linux-only here.
        // Native-first tools are excluded and checked above.
        // bash control flow
        "for", "while", "until", "case", "select",
        // shell builtins
        "cd", "pwd", "source", "alias", "unalias",
        "ls", "rm", "cp", "mv", "mkdir", "rmdir",
        "test", "true", "false", "exit",
        // json
        "jq",
    };
    if (bashCommands.Contains(firstWord, StringComparer.OrdinalIgnoreCase))
        return true;

    // Bash syntax markers anywhere in the command
    if (Regex.IsMatch(cmd, @"&&|\|\|")) return true;            // && or ||
    if (Regex.IsMatch(cmd, @";\s*(do|done|then|fi|esac)\b")) return true;
    if (Regex.IsMatch(cmd, @"<<\s*['""]?\w+['""]?")) return true;  // heredoc
    if (Regex.IsMatch(cmd, @"^#!")) return true;                // shebang
    if (Regex.IsMatch(cmd, @"\bif\s+\[")) return true;          // if [ -f ]
    if (Regex.IsMatch(cmd, @"2>/dev/null")) return true;        // bash redirect
    if (Regex.IsMatch(cmd, @">\s*/dev/null")) return true;
    if (Regex.IsMatch(cmd, @"/mnt/[a-z]/")) return true;        // WSL path
    if (Regex.IsMatch(cmd, @"^/[a-z]+/")) return true;          // absolute linux path

    // Default: PowerShell (safe fallback)
    return false;
}

// ============================================================
// Quoting danger detector
// ============================================================
static bool HasDangerousQuoting(string cmd)
{
    // Multi-line commands are almost always dangerous
    if (cmd.Contains('\n') || cmd.Contains('\r'))
        return true;
    
    // Count unescaped single quotes — odd number = unbalanced
    int singleQuotes = 0;
    for (int i = 0; i < cmd.Length; i++)
    {
        if (cmd[i] == '\'' && (i == 0 || cmd[i-1] != '`'))
            singleQuotes++;
    }
    if (singleQuotes % 2 != 0)
        return true;
    
    // JSON-style quoted arrays: ["...", "..."] or [\"...\", \"...\"]
    // These appear in Dockerfile ENTRYPOINT/CMD, curl -d payloads,
    // and inline scripts. PS 5.1 re-escapes the inner quotes, producing
    // [\"cmd\"] instead of ["cmd"], which breaks Docker exec-form and
    // JSON parsers.
    if (Regex.IsMatch(cmd, @"\[\\?""[^\]]*\\?""") ||     // ["..." or [\"...\"
        Regex.IsMatch(cmd, @"\[\s*'[^\]]*'\s*\]"))         // ['...']
        return true;

    // Heredoc markers — content after << is passed through PS which
    // re-interprets quotes, dollar signs, and backticks inside the
    // heredoc body. Always route to -File.
    if (Regex.IsMatch(cmd, @"<<\s*['""]?\w+['""]?"))
        return true;

    // Mixed quoting patterns that confuse PS 5.1:
    // Double-quoted string containing single quotes with special chars nearby
    // e.g., --notes "text with 'quotes' and $vars and `backticks`"
    bool hasDouble = cmd.Contains('"');
    bool hasSingle = cmd.Contains('\'');
    bool hasBacktick = cmd.Contains('`');
    bool hasDollar = cmd.Contains('$');
    
    // Three or more quote types mixed = danger zone
    int quoteTypes = (hasDouble ? 1 : 0) + (hasSingle ? 1 : 0) + (hasBacktick ? 1 : 0) + (hasDollar ? 1 : 0);
    if (quoteTypes >= 3)
        return true;
    
    // Very long commands with any quotes tend to have issues
    if (cmd.Length > 500 && (hasSingle || hasBacktick))
        return true;
    
    return false;
}

// ============================================================
// Process launchers
// ============================================================
static int RunPsViaFile(string command, bool debug)
{
    // Write the command to a temp .ps1 file and run with -File.
    // -File bypasses PowerShell's command-line argument parser entirely —
    // the file content is read as-is, no quote interpretation.
    string tempFile = Path.Combine(Path.GetTempPath(), $"shellfix_{Guid.NewGuid():N}.ps1");
    
    try
    {
        // Build the script content:
        // 1. Dot-source the shellfix profile so wrappers (grep, git stderr
        //    suppression, encoding fixes) are available in -File mode.
        // 2. Run the user's command.
        // 3. Propagate $LASTEXITCODE so the agent sees the real exit code
        //    from native executables, not PowerShell's own exit code.
        var sb = new System.Text.StringBuilder();
        string profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            @"WindowsPowerShell\shellfix_profile.ps1");
        if (File.Exists(profilePath))
        {
            sb.AppendLine($". '{profilePath}'");
        }
        sb.AppendLine(command);
        sb.AppendLine("exit $LASTEXITCODE");
        
        File.WriteAllText(tempFile, sb.ToString(), new System.Text.UTF8Encoding(false));
        
        if (debug) Console.Error.WriteLine($"[SHIM] Wrote temp script: {tempFile}");
        
        var startInfo = new ProcessStartInfo
        {
            FileName = GetPsBackend(),
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(tempFile);
        
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            Console.Error.WriteLine("[SHIM] Failed to start PowerShell via -File");
            return 127;
        }
        process.WaitForExit();
        return process.ExitCode;
    }
    finally
    {
        // Clean up temp file
        try { File.Delete(tempFile); } catch { }
    }
}

static int RunProcess(string exe, string[] arguments)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = exe,
        UseShellExecute = false,
    };
    foreach (var arg in arguments)
    {
        startInfo.ArgumentList.Add(arg);
    }

    using var process = Process.Start(startInfo);
    if (process is null)
    {
        Console.Error.WriteLine($"[SHIM] Failed to start: {exe}");
        return 127;
    }
    process.WaitForExit();
    return process.ExitCode;
}

static bool TryRunNativeInlineCommand(string command, bool debug, out int exitCode)
{
    exitCode = 0;
    if (!TryParseNativeInlineCommand(command, out string tool, out string scriptExtension, out string code, out List<string> remainingArgs))
        return false;

    string? nativePath = ResolveNativeCommand(tool);
    if (nativePath is null)
        return false;

    exitCode = RunNativeInlineCommand(nativePath, scriptExtension, code, remainingArgs, debug);
    return true;
}

static int RunNativeInlineCommand(string nativePath, string scriptExtension, string code, List<string> remainingArgs, bool debug)
{
    string tempFile = Path.Combine(Path.GetTempPath(), $"shellfix_inline_{Guid.NewGuid():N}{scriptExtension}");
    try
    {
        File.WriteAllText(tempFile, code, new UTF8Encoding(false));
        var args = new List<string> { tempFile };
        args.AddRange(remainingArgs);
        if (debug)
        {
            Console.Error.WriteLine($"[SHIM] Native inline: {nativePath} {tempFile}");
        }
        return RunNativeProcess(nativePath, args);
    }
    finally
    {
        try { File.Delete(tempFile); } catch { }
    }
}

static int RunNativeProcess(string exe, IEnumerable<string> arguments)
{
    string extension = Path.GetExtension(exe);
    var startInfo = new ProcessStartInfo
    {
        UseShellExecute = false,
    };

    if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
    {
        startInfo.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/s");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(QuoteForCmd(exe) + " " + string.Join(" ", arguments.Select(QuoteForCmd)));
    }
    else
    {
        startInfo.FileName = exe;
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }
    }

    using var process = Process.Start(startInfo);
    if (process is null)
    {
        Console.Error.WriteLine($"[SHIM] Failed to start native command: {exe}");
        return 127;
    }
    process.WaitForExit();
    return process.ExitCode;
}

static bool TryRunNativeDirectCommand(string command, bool debug, out int exitCode)
{
    exitCode = 0;
    if (!TryParseNativeDirectCommand(command, out string nativePath, out List<string> arguments))
        return false;

    if (debug)
    {
        Console.Error.WriteLine($"[SHIM] Native direct: {nativePath} {string.Join(" ", arguments)}");
    }

    exitCode = RunNativeProcess(nativePath, arguments);
    return true;
}

static bool TryParseNativeDirectCommand(string command, out string nativePath, out List<string> arguments)
{
    nativePath = "";
    arguments = new List<string>();

    string trimmed = StripSimplePowerShellNativeRedirection(command.Trim());
    if (string.IsNullOrWhiteSpace(trimmed))
        return false;

    int index = 0;
    if (trimmed[index] == '&')
    {
        index++;
        SkipWhitespace(trimmed, ref index);
    }

    if (!TryReadCommandToken(trimmed, ref index, out string firstToken))
        return false;

    string? resolved = ResolveNativeCommand(firstToken);
    if (resolved is null || !File.Exists(resolved))
        return false;

    if (!IsNativeFirstCommand(resolved))
        return false;

    string remainder = index < trimmed.Length ? trimmed[index..].Trim() : "";
    if (ContainsPowerShellOnlySyntax(remainder))
        return false;

    arguments = string.IsNullOrWhiteSpace(remainder) ? new List<string>() : ParseCommandArgs(remainder);
    nativePath = resolved;
    return true;
}

static string StripSimplePowerShellNativeRedirection(string command)
{
    // This is the Antigravity/D2 false-positive shape. Removing it lets the
    // shim preserve the real native exit code without PowerShell converting
    // stderr lines into NativeCommandError records.
    return Regex.Replace(command, @"\s+2>\s*&1\s*$", "", RegexOptions.IgnoreCase).TrimEnd();
}

static bool ContainsPowerShellOnlySyntax(string commandRemainder)
{
    if (string.IsNullOrWhiteSpace(commandRemainder))
        return false;

    // Keep direct native execution intentionally narrow. Shell operators mean
    // the command needs a real shell, not ArgumentList.
    return Regex.IsMatch(commandRemainder, @"(^|[^`]);|&&|\|\||(?<!\|)\|(?!\|)");
}

static string QuoteForCmd(string value)
{
    if (value.Length == 0) return "\"\"";
    if (!Regex.IsMatch(value, @"[\s""&()<>^|]")) return value;
    return "\"" + value.Replace("\"", "\\\"") + "\"";
}

static bool TryParseNativeInlineCommand(
    string command,
    out string tool,
    out string scriptExtension,
    out string code,
    out List<string> remainingArgs)
{
    tool = "";
    scriptExtension = "";
    code = "";
    remainingArgs = new List<string>();

    int index = 0;
    if (!TryReadCommandToken(command, ref index, out string firstToken))
        return false;

    string normalized = NormalizeCommandName(firstToken);
    string expectedSwitch;
    if (normalized is "python" or "python3" or "py")
    {
        expectedSwitch = "-c";
        scriptExtension = ".py";
    }
    else if (normalized is "node")
    {
        expectedSwitch = "-e";
        scriptExtension = ".js";
    }
    else
    {
        return false;
    }

    SkipWhitespace(command, ref index);
    if (!TryReadCommandToken(command, ref index, out string switchToken) ||
        !switchToken.Equals(expectedSwitch, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    SkipWhitespace(command, ref index);
    if (!TryReadInlinePayload(command, ref index, out code))
        return false;

    string remainder = index < command.Length ? command[index..].Trim() : "";
    remainingArgs = string.IsNullOrWhiteSpace(remainder) ? new List<string>() : ParseCommandArgs(remainder);
    tool = firstToken;
    return true;
}

static bool LooksLikeNativeInlineStart(string command)
{
    int index = 0;
    if (!TryReadCommandToken(command, ref index, out string firstToken))
        return false;

    string normalized = NormalizeCommandName(firstToken);
    string expectedSwitch = normalized switch
    {
        "python" or "python3" or "py" => "-c",
        "node" => "-e",
        _ => ""
    };
    if (expectedSwitch.Length == 0)
        return false;

    SkipWhitespace(command, ref index);
    return TryReadCommandToken(command, ref index, out string switchToken) &&
        switchToken.Equals(expectedSwitch, StringComparison.OrdinalIgnoreCase);
}

static bool TryReadCommandToken(string input, ref int index, out string token)
{
    token = "";
    SkipWhitespace(input, ref index);
    if (index >= input.Length)
        return false;

    var sb = new StringBuilder();
    char quote = input[index] is '"' or '\'' ? input[index++] : '\0';
    while (index < input.Length)
    {
        char ch = input[index];
        if (quote == '\0')
        {
            if (char.IsWhiteSpace(ch))
                break;
            sb.Append(ch);
            index++;
            continue;
        }

        if (ch == quote)
        {
            index++;
            break;
        }
        sb.Append(ch);
        index++;
    }

    token = sb.ToString();
    return token.Length > 0;
}

static bool TryReadInlinePayload(string input, ref int index, out string payload)
{
    payload = "";
    SkipWhitespace(input, ref index);
    if (index >= input.Length)
        return false;

    if (input[index] != '"' && input[index] != '\'')
    {
        return TryReadCommandToken(input, ref index, out payload);
    }

    char quote = input[index++];
    var sb = new StringBuilder();
    while (index < input.Length)
    {
        char ch = input[index];
        if (quote == '"' && ch == '\\' && index + 1 < input.Length && input[index + 1] == '"')
        {
            sb.Append('"');
            index += 2;
            continue;
        }
        if (ch == quote)
        {
            index++;
            payload = sb.ToString();
            return true;
        }
        sb.Append(ch);
        index++;
    }

    return false;
}

static void SkipWhitespace(string input, ref int index)
{
    while (index < input.Length && char.IsWhiteSpace(input[index]))
        index++;
}

static bool IsNativeFirstCommand(string commandName)
{
    string normalized = NormalizeCommandName(commandName);
    string[] nativeFirst =
    {
        "python", "python3", "py", "pip", "pip3", "node", "npm", "npx",
        "git", "gh", "dotnet", "docker", "kubectl", "cargo", "rustc",
        "d2", "dot"
    };
    return nativeFirst.Contains(normalized, StringComparer.OrdinalIgnoreCase);
}

static string NormalizeCommandName(string commandName)
{
    string fileName = Path.GetFileName(commandName.Trim('"', '\''));
    string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
    return string.IsNullOrWhiteSpace(withoutExtension) ? fileName.ToLowerInvariant() : withoutExtension.ToLowerInvariant();
}

static string? ResolveNativeCommand(string commandName)
{
    string trimmed = commandName.Trim('"', '\'');
    if ((Path.IsPathRooted(trimmed) || trimmed.Contains('\\') || trimmed.Contains('/')) && File.Exists(trimmed))
        return Path.GetFullPath(trimmed);

    var extensions = new List<string>();
    if (Path.HasExtension(trimmed))
    {
        extensions.Add("");
    }
    else
    {
        string pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD;.PS1";
        extensions.AddRange(pathext.Split(';', StringSplitOptions.RemoveEmptyEntries));
        extensions.Add("");
    }

    string? path = BuildRefreshedPath();
    if (string.IsNullOrWhiteSpace(path))
        return null;

    foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
        foreach (string ext in extensions)
        {
            string candidate;
            try
            {
                candidate = Path.Combine(dir.Trim(), trimmed + ext);
            }
            catch
            {
                continue;
            }
            if (File.Exists(candidate))
                return candidate;
        }
    }

    return null;
}

static string BuildRefreshedPath()
{
    var parts = new List<string>();
    foreach (var value in new[]
    {
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine),
        Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User)
    })
    {
        if (string.IsNullOrWhiteSpace(value))
            continue;

        foreach (var part in value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            if (!parts.Contains(part.Trim(), StringComparer.OrdinalIgnoreCase))
                parts.Add(part.Trim());
        }
    }

    return string.Join(Path.PathSeparator, parts);
}

static int RunWslBash(string command, bool debug)
{
    string wslDistro = GetWslDistro();

    // --- Pre-flight: verify WSL is available ---
    if (!File.Exists(WslExe))
    {
        Console.Error.WriteLine("[SHIM] wsl.exe not found. Falling back to PowerShell.");
        return RunProcess(GetPsBackend(), new[] { "-NoProfile", "-Command", command });
    }

    // --- Already-wrapped WSL commands: passthrough ---
    // If the command is already "wsl -d Ubuntu-24.04 -- bash -c '...'",
    // don't re-wrap it. Parse the existing wsl arguments and pass through.
    if (IsAlreadyWslWrapped(command))
    {
        if (debug) Console.Error.WriteLine("[SHIM] Already WSL-wrapped, passthrough");
        return RunWslPassthrough(command, debug);
    }

    // Order matters:
    // 1. Translate Windows paths to WSL paths
    //    (this may add single-quoted paths like '/mnt/c/...')
    string translated = TranslatePaths(command);
    
    // 2. Re-quote bare glob patterns that lost quotes during Windows arg parsing
    //    e.g., -name *.py → -name '*.py'
    //    Match: a flag like -name/-iname/-path followed by a glob pattern
    translated = Regex.Replace(translated, @"(-(?:name|iname|path|regex|wholename)\s+)(\*[^\s'""]+|[^\s'""]*\*[^\s'""]*)", "$1'$2'");
    //    Also quote standalone globs after common commands (less common but possible)
    //    e.g., ls *.py → ls '*.py' (but only if not already quoted)

    // 3. Escape $ so the WSL launch layer does not expand bash variables
    //    before the requested bash -c payload runs.
    translated = Regex.Replace(translated, @"(?<!\\)\$", @"\$");

    var startInfo = new ProcessStartInfo
    {
        FileName = WslExe,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add("-d");
    startInfo.ArgumentList.Add(wslDistro);
    startInfo.ArgumentList.Add("--");
    startInfo.ArgumentList.Add("bash");
    startInfo.ArgumentList.Add("-c");
    startInfo.ArgumentList.Add(translated);
    
    // Ensure WSL_UTF8 is set in the process environment
    startInfo.Environment["WSL_UTF8"] = "1";

    if (debug) Console.Error.WriteLine($"[SHIM] Running: wsl -d {wslDistro} -- bash -c \"{translated.Substring(0, Math.Min(100, translated.Length))}...\"");

    try
    {
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            Console.Error.WriteLine("[SHIM] Failed to start WSL. Falling back to PowerShell.");
            return RunProcess(GetPsBackend(), new[] { "-NoProfile", "-Command", command });
        }
        process.WaitForExit();
        return process.ExitCode;
    }
    catch (Exception ex)
    {
        // WSL failed to launch (crashed, not installed, etc.)
        // Fall back to PowerShell so the agent isn't stuck
        Console.Error.WriteLine($"[SHIM] WSL exception: {ex.Message}. Falling back to PowerShell.");
        return RunProcess(GetPsBackend(), new[] { "-NoProfile", "-Command", command });
    }
}

static string GetWslDistro()
{
    string? configured = Environment.GetEnvironmentVariable("SHELLFIX_WSL_DISTRO");
    if (!string.IsNullOrWhiteSpace(configured)) return configured;
    return DefaultWslDistro;
}

static string TranslatePaths(string cmd)
{
    // Windows paths arrive with quotes stripped by the calling shell.
    // We need to find patterns like C:\Users\Aaron\... and convert them.
    //
    // Strategy: find drive-letter patterns and convert everything that
    // looks like a path segment (letters, digits, dots, hyphens, underscores,
    // spaces, and backslashes) until we hit something that can't be a path.
    //
    // Key insight: a space followed by a word that contains \ is still
    // part of the same path (e.g., "C:\Users\Aaron\Openclaw Project\monitor").
    // A space followed by a - (flag) or end-of-string is NOT a path.
    
    // Match: drive letter + :\ + greedy path chars
    // Path chars: anything except |<>;&& but we need to handle spaces specially.
    // A space is part of the path if the NEXT non-space segment contains \.
    string result = Regex.Replace(cmd, 
        @"([A-Za-z]):\\((?:[^\s\|<>;]|\s(?=[^\s\-\|<>;]*\\))*[^\s\|<>;])",
        m =>
    {
        char drive = char.ToLower(m.Groups[1].Value[0]);
        string rest = m.Groups[2].Value.Replace('\\', '/');
        string wslPath = $"/mnt/{drive}/{rest}";
        // If path contains spaces, single-quote it for bash
        if (wslPath.Contains(' '))
            return $"'{wslPath}'";
        return wslPath;
    });

    return result;
}

// ============================================================
// WSL passthrough for already-wrapped commands
// ============================================================
static bool IsAlreadyWslWrapped(string cmd)
{
    // Detect commands that are already wsl -d <distro> -- bash -c "..."
    // or wsl.exe -d <distro> -- bash -c "..."
    cmd = cmd.TrimStart();
    return Regex.IsMatch(cmd, @"^wsl(?:\.exe)?\s+", RegexOptions.IgnoreCase);
}

static int RunWslPassthrough(string command, bool debug)
{
    // Parse the wsl command into arguments, preserving quoted strings.
    // Input: wsl -d Ubuntu-24.04 -- bash -c "echo hello && echo world"
    // Output: ["-d", "Ubuntu-24.04", "--", "bash", "-c", "echo hello && echo world"]
    var wslArgs = ParseCommandArgs(command);
    
    // Remove the leading "wsl" or "wsl.exe" token
    if (wslArgs.Count > 0 && 
        (wslArgs[0].Equals("wsl", StringComparison.OrdinalIgnoreCase) ||
         wslArgs[0].Equals("wsl.exe", StringComparison.OrdinalIgnoreCase)))
    {
        wslArgs.RemoveAt(0);
    }

    // wsl.exe -- bash -c "..." can expand $var before the inner bash -c
    // sees it. Escape dollars in the payload only; bash receives the intended
    // variables and expands them in the right shell.
    for (int argIdx = 0; argIdx < wslArgs.Count - 1; argIdx++)
    {
        if (wslArgs[argIdx] == "-c")
        {
            string payload = wslArgs[argIdx + 1];
            payload = Regex.Replace(payload, @"(?<!\\)\$", @"\$");
            wslArgs[argIdx + 1] = payload;
            break;
        }
    }

    var startInfo = new ProcessStartInfo
    {
        FileName = WslExe,
        UseShellExecute = false,
    };
    foreach (var arg in wslArgs)
    {
        startInfo.ArgumentList.Add(arg);
    }
    startInfo.Environment["WSL_UTF8"] = "1";

    if (debug)
    {
        var preview = string.Join(" ", wslArgs.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        Console.Error.WriteLine($"[SHIM] WSL passthrough: wsl.exe {preview.Substring(0, Math.Min(120, preview.Length))}...");
    }

    try
    {
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            Console.Error.WriteLine("[SHIM] Failed to start WSL passthrough.");
            return 127;
        }
        process.WaitForExit();
        return process.ExitCode;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[SHIM] WSL passthrough exception: {ex.Message}");
        return 127;
    }
}

/// <summary>
/// Parse a command string into arguments, respecting double-quoted and
/// single-quoted strings. Handles escaped quotes within double-quoted strings.
/// </summary>
static List<string> ParseCommandArgs(string input)
{
    var args = new List<string>();
    int i = 0;
    while (i < input.Length)
    {
        // Skip whitespace
        while (i < input.Length && char.IsWhiteSpace(input[i])) i++;
        if (i >= input.Length) break;

        var token = new System.Text.StringBuilder();
        
        if (input[i] == '"')
        {
            // Double-quoted string: read until matching unescaped "
            i++; // skip opening "
            while (i < input.Length)
            {
                if (input[i] == '\\' && i + 1 < input.Length && input[i + 1] == '"')
                {
                    token.Append('"');
                    i += 2;
                }
                else if (input[i] == '"')
                {
                    i++; // skip closing "
                    break;
                }
                else
                {
                    token.Append(input[i]);
                    i++;
                }
            }
        }
        else if (input[i] == '\'')
        {
            // Single-quoted string: read until matching '
            i++; // skip opening '
            while (i < input.Length && input[i] != '\'')
            {
                token.Append(input[i]);
                i++;
            }
            if (i < input.Length) i++; // skip closing '
        }
        else
        {
            // Bare word: read until whitespace or quote
            while (i < input.Length && !char.IsWhiteSpace(input[i]))
            {
                // If we hit a quote mid-token, consume the quoted part
                if (input[i] == '"')
                {
                    i++; // skip "
                    while (i < input.Length)
                    {
                        if (input[i] == '\\' && i + 1 < input.Length && input[i + 1] == '"')
                        {
                            token.Append('"');
                            i += 2;
                        }
                        else if (input[i] == '"')
                        {
                            i++;
                            break;
                        }
                        else
                        {
                            token.Append(input[i]);
                            i++;
                        }
                    }
                }
                else
                {
                    token.Append(input[i]);
                    i++;
                }
            }
        }

        if (token.Length > 0)
        {
            args.Add(token.ToString());
        }
    }
    return args;
}

// ============================================================
// Session Proxy Mode
// ============================================================

/// <summary>
/// Spawns real powershell.exe as a child process with stdin redirected.
/// Each line from our stdin is inspected by RewriteForProxy() before
/// being sent to PS. Stdout and stderr pass through transparently.
/// </summary>
static int RunInteractiveProxy(string[] originalArgs, bool debug)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = GetPsBackend(),
        UseShellExecute = false,
        RedirectStandardInput = true,
        // Do NOT redirect stdout/stderr — let them flow directly to our
        // console so the IDE sees them natively (colors, prompts, etc.)
        RedirectStandardOutput = false,
        RedirectStandardError = false,
    };

    // Pass through any original args (like -NoExit, -NoProfile, -NoLogo)
    foreach (var arg in originalArgs)
    {
        startInfo.ArgumentList.Add(arg);
    }

    // Prevent infinite recursion: tell the child PS not to invoke the shim
    startInfo.Environment["PWSH_SHIM_BYPASS"] = "1";

    using var process = Process.Start(startInfo);
    if (process is null)
    {
        Console.Error.WriteLine("[SHIM] Failed to start PowerShell for proxy mode");
        return 127;
    }

    var psStdin = process.StandardInput;
    // Force UTF-8 no BOM on both sides:
    // - Our stdin: so Console.ReadLine() decodes BOM as U+FEFF (not 3 CP437 chars)
    // - Child PS stdin: so we don't inject a new BOM when writing
    Console.InputEncoding = new System.Text.UTF8Encoding(false);
    psStdin.AutoFlush = true;

    // Read lines from our stdin and forward (possibly rewritten) to PS.
    // Inline interpreter payloads are buffered and executed natively so
    // PowerShell never parses the Python/JS body.
    string? line;
    StringBuilder? inlineBuffer = null;
    StringBuilder? wslBuffer = null;
    while ((line = Console.ReadLine()) != null)
    {
        if (process.HasExited) break;

        // Strip UTF-8 BOM (U+FEFF) — IDE stdin encoders may inject
        // a BOM on the first write, turning "wsl" into "\uFEFFwsl"
        line = line.TrimStart('\uFEFF');

        if (inlineBuffer is not null)
        {
            inlineBuffer.AppendLine(line);
            string bufferedCommand = inlineBuffer.ToString().TrimEnd('\r', '\n');
            if (TryRunNativeInlineCommand(bufferedCommand, debug, out int bufferedInlineExitCode))
            {
                SetProxyLastExitCode(psStdin, bufferedInlineExitCode);
                inlineBuffer = null;
            }
            continue;
        }

        if (wslBuffer is not null)
        {
            wslBuffer.AppendLine(line);
            string bufferedCommand = wslBuffer.ToString().TrimEnd('\r', '\n');
            if (IsBufferedCommandComplete(bufferedCommand))
            {
                int exitCode = RunWslPassthrough(bufferedCommand, debug);
                SetProxyLastExitCode(psStdin, exitCode);
                wslBuffer = null;
            }
            continue;
        }

        if (LooksLikeNativeInlineStart(line))
        {
            if (TryRunNativeInlineCommand(line, debug, out int nativeInlineExitCode))
            {
                SetProxyLastExitCode(psStdin, nativeInlineExitCode);
                continue;
            }

            inlineBuffer = new StringBuilder();
            inlineBuffer.AppendLine(line);
            continue;
        }

        if (StartsWithWslCommand(line))
        {
            if (IsBufferedCommandComplete(line))
            {
                int exitCode = RunWslPassthrough(line, debug);
                SetProxyLastExitCode(psStdin, exitCode);
            }
            else
            {
                wslBuffer = new StringBuilder();
                wslBuffer.AppendLine(line);
            }
            continue;
        }

        if (TryRunNativeDirectCommand(line, debug, out int nativeDirectExitCode))
        {
            SetProxyLastExitCode(psStdin, nativeDirectExitCode);
            continue;
        }

        string rewritten = RewriteForProxy(line, debug);
        psStdin.WriteLine(rewritten);
    }

    if (inlineBuffer is not null && !process.HasExited)
    {
        psStdin.WriteLine(inlineBuffer.ToString());
    }
    if (wslBuffer is not null && !process.HasExited)
    {
        psStdin.WriteLine(wslBuffer.ToString());
    }

    // If stdin closed (IDE terminating), close PS stdin and wait
    try { psStdin.Close(); } catch { }
    process.WaitForExit();
    return process.ExitCode;
}

static void SetProxyLastExitCode(StreamWriter psStdin, int exitCode)
{
    try
    {
        psStdin.WriteLine($"$global:LASTEXITCODE = {exitCode}");
    }
    catch
    {
        // The child shell may already be exiting. The real process exit code
        // has already been returned to the IDE via the direct child process.
    }
}

static bool StartsWithWslCommand(string line)
{
    string trimmed = line.TrimStart();
    return trimmed.StartsWith("wsl ", StringComparison.OrdinalIgnoreCase) ||
           trimmed.StartsWith("wsl.exe ", StringComparison.OrdinalIgnoreCase);
}

static bool IsBufferedCommandComplete(string command)
{
    bool inSingle = false;
    bool inDouble = false;
    bool escaped = false;

    foreach (char ch in command)
    {
        if (escaped)
        {
            escaped = false;
            continue;
        }

        if (ch == '\\' && inDouble)
        {
            escaped = true;
            continue;
        }

        if (ch == '\'' && !inDouble)
        {
            inSingle = !inSingle;
            continue;
        }

        if (ch == '"' && !inSingle)
        {
            inDouble = !inDouble;
        }
    }

    return !inSingle && !inDouble;
}

/// <summary>
/// Inspects a single line of stdin and rewrites it if it contains
/// WSL/bash commands with PS 5.1-problematic tokens.
///
/// Strategy: detect lines that start with wsl/wsl.exe and contain
/// tokens that PS 5.1 would choke on (&&, ||, [N:-N], complex nested
/// quotes). Rewrite them to use --% stop-parsing only when the backend
/// is PS 5.1; pwsh 7 parses these WSL lines correctly and --% can break
/// nested bash/Python quoting there.
///
/// Lines that are pure PowerShell pass through unchanged.
/// </summary>
static string RewriteForProxy(string line, bool debug)
{
    string trimmed = line.TrimStart();
    if (string.IsNullOrEmpty(trimmed)) return line;

    // --- Detect WSL commands that need rewriting ---
    // Pattern: wsl [-d distro] [--] bash -c "...problematic tokens..."
    // Also catches: wsl.exe, bare wsl calls with bash -c
    bool startsWithWsl = trimmed.StartsWith("wsl ", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.StartsWith("wsl.exe ", StringComparison.OrdinalIgnoreCase);

    if (!startsWithWsl)
    {
        return line; // Not a WSL command — let the profile wrappers handle it
    }

    // --- WSL command detected. Check if it has problematic tokens ---
    bool hasProblematic = HasProblematicTokens(trimmed);

    if (!hasProblematic)
    {
        return line; // WSL command but no problematic tokens — safe
    }

    if (IsPwsh7Backend())
    {
        if (debug) Console.Error.WriteLine("[SHIM-PROXY] pwsh backend, leaving WSL command unchanged");
        return line;
    }

    // --- Rewrite: inject --% after wsl/wsl.exe ---
    // Input:  wsl -d Ubuntu-24.04 -- bash -c "echo hello && echo world"
    // Output: wsl.exe --% -d Ubuntu-24.04 -- bash -c "echo hello && echo world"
    string rewrittenLine;
    if (trimmed.StartsWith("wsl.exe ", StringComparison.OrdinalIgnoreCase))
    {
        // Already wsl.exe — inject --% right after
        rewrittenLine = "wsl.exe --% " + trimmed.Substring("wsl.exe ".Length);
    }
    else
    {
        // wsl → wsl.exe --% (must use .exe for --% to work with native commands)
        rewrittenLine = "wsl.exe --% " + trimmed.Substring("wsl ".Length);
    }

    // Preserve leading whitespace from original line
    string leadingWs = line.Substring(0, line.Length - line.TrimStart().Length);
    rewrittenLine = leadingWs + rewrittenLine;

    if (debug) Console.Error.WriteLine($"[SHIM-PROXY] Rewrite: {rewrittenLine.Substring(0, Math.Min(120, rewrittenLine.Length))}...");
    return rewrittenLine;
}

/// <summary>
/// Check if a line contains tokens that PS 5.1 would reject at parse time.
/// </summary>
static bool HasProblematicTokens(string line)
{
    // && and || — PS 5.1 doesn't support pipeline chain operators
    // But we need to check they're not inside a quoted string
    // Simple heuristic: if the line contains && or || outside of PS-style
    // string contexts, it's problematic
    if (Regex.IsMatch(line, @"&&|\|\|")) return true;

    // [N:-N] or [N:N] — PS interprets as array index/slice
    if (Regex.IsMatch(line, @"\[\d+:-?\d+\]")) return true;

    // JSON-style quoted arrays: ["...", "..."] — PS re-escapes inner
    // quotes when passing through -Command, producing [\"...\"] instead
    // of ["..."]. This breaks Dockerfile ENTRYPOINT exec-form, JSON
    // payloads in curl -d, and similar patterns.
    if (Regex.IsMatch(line, @"\[\\?""[^\]]*\\?""")) return true;

    // Heredoc markers: << 'EOF', <<EOF, <<-EOF — the heredoc body
    // flows through PS which will mangle quotes and dollar signs.
    if (Regex.IsMatch(line, @"<<-?\s*['""]?\w+['""]?")) return true;

    // Nested single quotes inside double quotes with bash-specific patterns
    // e.g., bash -c "python3 -c 'print(...)'"
    if (line.Contains("bash") && line.Contains("-c") &&
        line.Contains("'") && line.Contains("\""))
        return true;

    return false;
}

static bool IsPwsh7Backend()
{
    string backend = Path.GetFileName(GetPsBackend());
    return backend.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase);
}

// ============================================================
// PowerShell backend accessor
// ============================================================
static string GetPsBackend()
{
    return Environment.GetEnvironmentVariable("SHELLFIX_PS_BACKEND")
        ?? @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
}
