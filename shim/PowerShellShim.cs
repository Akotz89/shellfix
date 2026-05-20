using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// PowerShell Shim v4 — intercepts `powershell -Command "..."` calls from the
/// Antigravity IDE agent and routes bash-looking commands to WSL bash.
/// PowerShell-looking commands pass through to the real powershell.exe.
///
/// Install: compile to powershell.exe and place in a PATH directory that
/// precedes C:\Windows\System32\WindowsPowerShell\v1.0\.
///
/// Kill switch: set environment variable PWSH_SHIM_BYPASS=1 to disable.
/// Debug mode: set PWSH_SHIM_DEBUG=1 to log decisions to stderr.
/// </summary>

const string RealPowerShell = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
const string WslExe = @"C:\Windows\System32\wsl.exe";
const string WslDistro = "Ubuntu-24.04";

// --- Kill switch ---
if (Environment.GetEnvironmentVariable("PWSH_SHIM_BYPASS") == "1")
{
    return RunProcess(RealPowerShell, args);
}

bool debug = Environment.GetEnvironmentVariable("PWSH_SHIM_DEBUG") == "1";

// --- Extract the command string ---
// IDE calls: powershell -Command "the whole command"
// We need to find -Command and grab everything after it.
string? commandStr = null;
var otherArgs = new List<string>();
bool foundCommand = false;

for (int i = 0; i < args.Length; i++)
{
    if (!foundCommand && args[i].Equals("-Command", StringComparison.OrdinalIgnoreCase))
    {
        foundCommand = true;
        // Everything after -Command is the command string
        // It might be one arg (quoted) or multiple args joined
        commandStr = string.Join(" ", args.Skip(i + 1));
        break;
    }
    otherArgs.Add(args[i]);
}

// If no -Command flag, pass through to real PowerShell
if (!foundCommand || string.IsNullOrWhiteSpace(commandStr))
{
    if (debug) Console.Error.WriteLine($"[SHIM] No -Command found, passthrough: {string.Join(" ", args)}");
    return RunProcess(RealPowerShell, args);
}

// --- Classify: bash or PowerShell? ---
bool isBash = LooksLikeBash(commandStr);

if (debug) Console.Error.WriteLine($"[SHIM] Classified as {(isBash ? "BASH" : "PS")}: {commandStr.Substring(0, Math.Min(80, commandStr.Length))}...");

if (isBash)
{
    // Route to WSL bash
    return RunWslBash(commandStr, debug);
}
else
{
    // Pass through to real PowerShell
    return RunProcess(RealPowerShell, args);
}

// ============================================================
// Heuristic classifier
// ============================================================
static bool LooksLikeBash(string cmd)
{
    cmd = cmd.Trim();
    if (string.IsNullOrEmpty(cmd)) return false;

    // Extract the first word/token
    string firstWord = cmd.Split(new[] { ' ', '\t', '\r', '\n' }, 2, StringSplitOptions.RemoveEmptyEntries)[0];

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
        // dev tools
        "python3", "pip3", "python", "pip", "npm", "node", "npx",
        "git", "make", "gcc", "g++", "cargo", "rustc",
        "docker", "docker-compose", "kubectl",
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
// Process launchers
// ============================================================
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

static int RunWslBash(string command, bool debug)
{
    // --- Pre-flight: verify WSL is available ---
    if (!File.Exists(WslExe))
    {
        Console.Error.WriteLine("[SHIM] wsl.exe not found. Falling back to PowerShell.");
        return RunProcess(RealPowerShell, new[] { "-NoProfile", "-Command", command });
    }

    // Order matters:
    // 1. Escape user-provided single quotes (e.g., grep "it's")
    //    backslash-escape so bash sees \' as a literal quote
    string escaped = command.Replace("'", @"\'");
    
    // 2. Translate Windows paths to WSL paths
    //    (this may add single-quoted paths like '/mnt/c/...')
    string translated = TranslatePaths(escaped);
    
    // 3. Re-quote bare glob patterns that lost quotes during Windows arg parsing
    //    e.g., -name *.py → -name '*.py'
    //    Match: a flag like -name/-iname/-path followed by a glob pattern
    translated = Regex.Replace(translated, @"(-(?:name|iname|path|regex|wholename)\s+)(\*[^\s'""]+|[^\s'""]*\*[^\s'""]*)", "$1'$2'");
    //    Also quote standalone globs after common commands (less common but possible)
    //    e.g., ls *.py → ls '*.py' (but only if not already quoted)
    
    // 4. Escape $ so bash -c doesn't expand positional params
    //    Preserve already-escaped \$ sequences
    translated = Regex.Replace(translated, @"(?<!\\)\$", @"\$");

    var startInfo = new ProcessStartInfo
    {
        FileName = WslExe,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add("-d");
    startInfo.ArgumentList.Add(WslDistro);
    startInfo.ArgumentList.Add("--");
    startInfo.ArgumentList.Add("bash");
    startInfo.ArgumentList.Add("-c");
    startInfo.ArgumentList.Add(translated);
    
    // Ensure WSL_UTF8 is set in the process environment
    startInfo.Environment["WSL_UTF8"] = "1";

    if (debug) Console.Error.WriteLine($"[SHIM] Running: wsl -d {WslDistro} -- bash -c \"{translated.Substring(0, Math.Min(100, translated.Length))}...\"");

    try
    {
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            Console.Error.WriteLine("[SHIM] Failed to start WSL. Falling back to PowerShell.");
            return RunProcess(RealPowerShell, new[] { "-NoProfile", "-Command", command });
        }
        process.WaitForExit();
        return process.ExitCode;
    }
    catch (Exception ex)
    {
        // WSL failed to launch (crashed, not installed, etc.)
        // Fall back to PowerShell so the agent isn't stuck
        Console.Error.WriteLine($"[SHIM] WSL exception: {ex.Message}. Falling back to PowerShell.");
        return RunProcess(RealPowerShell, new[] { "-NoProfile", "-Command", command });
    }
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
