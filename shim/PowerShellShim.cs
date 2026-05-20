using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

const string RealPowerShell = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
const string WslExe = @"C:\Windows\System32\wsl.exe";
const string WslDistro = "Ubuntu-24.04";

// --- Kill switch ---
if (Environment.GetEnvironmentVariable("PWSH_SHIM_BYPASS") == "1")
{
    return RunProcess(RealPowerShell, args);
}

bool debug = Environment.GetEnvironmentVariable("PWSH_SHIM_DEBUG") == "1";

// --- Extract the command string from RAW command line ---
// CRITICAL: We must NOT rely on args[] because PowerShell has already
// tokenized them. Tokens like &&, [1:-1], and nested single quotes
// cause PS parser errors BEFORE our Main() runs with parsed args.
// Instead, read the raw command line and extract -Command payload.
string? commandStr = null;
bool foundCommand = false;

// First try raw command line (bypasses all PS parsing)
string rawCmdLine = Environment.CommandLine;
if (debug) Console.Error.WriteLine($"[SHIM] Raw cmdline: {rawCmdLine.Substring(0, Math.Min(200, rawCmdLine.Length))}...");

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
    // Unescape \" → " (IDE often escapes inner quotes)
    commandStr = commandStr.Replace("\\\"", "\"");
}
else
{
    // Fallback: try args[] for non-standard invocations
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i].Equals("-Command", StringComparison.OrdinalIgnoreCase))
        {
            foundCommand = true;
            commandStr = string.Join(" ", args.Skip(i + 1));
            break;
        }
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

// --- Classify: bash or PowerShell? ---
bool isBash = LooksLikeBash(commandStr);

if (debug) Console.Error.WriteLine($"[SHIM] Classified as {(isBash ? "BASH" : "PS")}: {commandStr.Substring(0, Math.Min(80, commandStr.Length))}...");

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
    // PS command — check if the command string has quoting that will
    // break PowerShell's -Command parser (mixed quotes, multi-line,
    // unmatched single quotes, embedded backticks in strings, etc.)
    if (HasDangerousQuoting(commandStr))
    {
        if (debug) Console.Error.WriteLine("[SHIM] Dangerous quoting detected, using -File mode");
        return RunPsViaFile(commandStr, debug);
    }
    // Simple PS command — pass through directly
    return RunProcess(RealPowerShell, args);
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
        File.WriteAllText(tempFile, command, new System.Text.UTF8Encoding(false));
        
        if (debug) Console.Error.WriteLine($"[SHIM] Wrote temp script: {tempFile}");
        
        var startInfo = new ProcessStartInfo
        {
            FileName = RealPowerShell,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-NoProfile");
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

static int RunWslBash(string command, bool debug)
{
    // --- Pre-flight: verify WSL is available ---
    if (!File.Exists(WslExe))
    {
        Console.Error.WriteLine("[SHIM] wsl.exe not found. Falling back to PowerShell.");
        return RunProcess(RealPowerShell, new[] { "-NoProfile", "-Command", command });
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
        FileName = RealPowerShell,
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
    // Match PS encoding — UTF-8 no BOM
    psStdin.AutoFlush = true;

    // Read lines from our stdin and forward (possibly rewritten) to PS
    string? line;
    while ((line = Console.ReadLine()) != null)
    {
        if (process.HasExited) break;

        string rewritten = RewriteForProxy(line, debug);
        psStdin.WriteLine(rewritten);
    }

    // If stdin closed (IDE terminating), close PS stdin and wait
    try { psStdin.Close(); } catch { }
    process.WaitForExit();
    return process.ExitCode;
}

/// <summary>
/// Inspects a single line of stdin and rewrites it if it contains
/// WSL/bash commands with PS 5.1-problematic tokens.
///
/// Strategy: detect lines that start with wsl/wsl.exe and contain
/// tokens that PS 5.1 would choke on (&&, ||, [N:-N], complex nested
/// quotes). Rewrite them to use --% stop-parsing token so PS passes
/// everything literally to wsl.exe.
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

    // Nested single quotes inside double quotes with bash-specific patterns
    // e.g., bash -c "python3 -c 'print(...)'"
    if (line.Contains("bash") && line.Contains("-c") &&
        line.Contains("'") && line.Contains("\""))
        return true;

    return false;
}

/// <summary>
/// Check if a non-WSL line looks like a bash command AND has problematic tokens.
/// This catches cases where the agent sends bare bash commands like:
///   grep "pattern" file && echo "found"
///   python3 -c 'print(x[1:-1])'
/// </summary>
static bool LooksLikeBashWithProblematicTokens(string line)
{
    if (!HasProblematicTokens(line)) return false;
    if (!LooksLikeBash(line)) return false;

    // Extra safety: don't rewrite lines that look like they're using
    // PS-specific features even if they have &&
    if (line.Contains("$?") || line.Contains("$LASTEXITCODE")) return false;

    return true;
}

/// <summary>
/// Escape a bare command string for embedding in bash -c "...".
/// Escapes double quotes and dollar signs.
/// </summary>
static string EscapeForBashC(string cmd)
{
    return cmd.Replace("\\", "\\\\")
              .Replace("\"", "\\\"")
              .Replace("$", "\\$");
}
