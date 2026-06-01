namespace Shellfix.Cli;

internal sealed class DoctorCommand
{
    private readonly ShellfixContext _context;

    public DoctorCommand(ShellfixContext context)
    {
        _context = context;
    }

    public int Run(CommandOptions options)
    {
        var report = BuildReport(_context);
        if (options.Has("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(report, Json.Options));
            return report.Checks.Any(c => c.Status == "fail") ? 1 : 0;
        }

        Console.WriteLine($"Shellfix doctor ({report.Version})");
        Console.WriteLine();
        WriteChecks(report.Checks);
        return report.Checks.Any(c => c.Status == "fail") ? 1 : 0;
    }

    public static DoctorReport BuildReport(ShellfixContext context)
    {
        var state = new StateStore(context).Load();
        var installRoot = state?.InstallRoot ?? context.ProgramFilesShellfix;
        var shimPath = state?.ShimPath ?? Path.Combine(context.DefaultBinDir, "powershell.exe");
        var wslDistro = state?.WslDistro ?? Environment.GetEnvironmentVariable("SHELLFIX_WSL_DISTRO") ?? "Ubuntu-24.04";
        var checks = new List<CheckResult>();

        checks.Add(Check("state", File.Exists(context.StatePath), $"State file: {context.StatePath}", "Run shellfix install."));
        checks.Add(Check("cli", File.Exists(Path.Combine(installRoot, "shellfix.exe")) || File.Exists(context.ExecutablePath), $"CLI: {context.ExecutablePath}", "Run install.ps1 or shellfix install."));
        checks.Add(Check("shim", File.Exists(shimPath), $"Shim: {shimPath}", "Run shellfix install."));
        checks.Add(Check("shim-hash", File.Exists(shimPath), File.Exists(shimPath) ? $"SHA256: {Hashing.Sha256File(shimPath)[..12]}..." : "Shim hash unavailable", "Run shellfix install."));
        checks.Add(CheckInstallDrift(context, installRoot, shimPath));
        checks.Add(WslManager.Check(wslDistro));
        checks.Add(CheckNativeToolRouting());
        checks.Add(CheckDirectNativeRouting());
        checks.Add(new PathManager().Check(shimPath, installRoot, state?.BinDir ?? context.DefaultBinDir));
        checks.Add(ProfileInstaller.Check(context, state));
        checks.AddRange(new ShortcutManager(context).Check(state));
        checks.Add(AntigravitySettingsManager.Check(context, state));
        checks.Add(CheckAntigravityRuntimeProcesses(shimPath));

        return new DoctorReport
        {
            Version = context.Version,
            InstallRoot = installRoot,
            StatePath = context.StatePath,
            ShimPath = shimPath,
            ShimHash = File.Exists(shimPath) ? Hashing.Sha256File(shimPath) : "",
            PowerShellBackend = PowerShellBackend.Describe(),
            WslDistro = wslDistro,
            Checks = checks
        };
    }

    public static void WriteChecks(IEnumerable<CheckResult> checks)
    {
        foreach (var check in checks)
        {
            var label = check.Status.ToUpperInvariant().PadRight(4);
            Console.WriteLine($"[{label}] {check.Name}: {check.Message}");
            if (check.Status != "pass" && !string.IsNullOrWhiteSpace(check.Remediation))
            {
                Console.WriteLine($"       {check.Remediation}");
            }
        }
    }

    private static CheckResult Check(string name, bool pass, string message, string remediation) =>
        new()
        {
            Name = name,
            Status = pass ? "pass" : "fail",
            Message = message,
            Remediation = pass ? "" : remediation
        };

    private static CheckResult CheckNativeToolRouting()
    {
        var tools = new[] { "python", "python3", "node", "npx" };
        var resolved = new List<string>();
        var missing = new List<string>();

        foreach (var tool in tools)
        {
            var path = ResolveWindowsCommand(tool);
            if (string.IsNullOrWhiteSpace(path))
            {
                missing.Add(tool);
            }
            else
            {
                resolved.Add($"{tool}={path}");
            }
        }

        if (missing.Count == 0)
        {
            return new CheckResult
            {
                Name = "native-tools",
                Status = "pass",
                Message = string.Join("; ", resolved),
                Remediation = ""
            };
        }

        return new CheckResult
        {
            Name = "native-tools",
            Status = "warn",
            Message = $"Missing native tools: {string.Join(", ", missing)}. Found: {string.Join("; ", resolved)}",
            Remediation = "Install the missing Windows developer tools or use explicit wsl commands for Linux tools."
        };
    }

    private static CheckResult CheckInstallDrift(ShellfixContext context, string installRoot, string shimPath)
    {
        var repoShim = Path.Combine(context.RepoRoot, "shim", "out", "powershell.exe");
        var installedCli = Path.Combine(installRoot, "shellfix.exe");
        var messages = new List<string>();
        var drift = false;

        if (File.Exists(repoShim) && File.Exists(shimPath))
        {
            var repoHash = Hashing.Sha256File(repoShim);
            var installedHash = Hashing.Sha256File(shimPath);
            messages.Add($"repo-shim={repoHash[..12]} installed-shim={installedHash[..12]}");
            drift |= !repoHash.Equals(installedHash, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            messages.Add("repo shim output or installed shim missing; drift comparison skipped");
        }

        if (File.Exists(installedCli))
        {
            messages.Add($"installed-cli={installedCli}");
        }
        else
        {
            messages.Add("installed CLI not found in install root");
            drift = true;
        }

        return new CheckResult
        {
            Name = "install-drift",
            Status = drift ? "warn" : "pass",
            Message = string.Join("; ", messages),
            Remediation = drift ? "Run dotnet publish for shim/CLI, then reinstall with install.ps1 -SkipBuild or shellfix install --skip-build." : ""
        };
    }

    private static CheckResult CheckDirectNativeRouting()
    {
        var noisyTools = new[] { "d2", "dot" };
        var found = noisyTools
            .Select(tool => new { Tool = tool, Path = ResolveWindowsCommand(tool), Candidates = ResolveWindowsCommands(tool) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .ToList();

        if (found.Count == 0)
        {
            return new CheckResult
            {
                Name = "native-direct",
                Status = "pass",
                Message = "Full-path native routing is enabled. No optional noisy-stderr tools were found for additional route display.",
                Remediation = ""
            };
        }

        var toolNotes = found.Select(item =>
            item.Candidates.Count > 1
                ? $"{item.Tool} candidates={string.Join(" | ", item.Candidates)}"
                : $"{item.Tool} path={item.Path}");
        return new CheckResult
        {
            Name = "native-direct",
            Status = "pass",
            Message = $"Full-path native routing enabled for known developer tools; noisy-stderr tools: {string.Join("; ", toolNotes)}",
            Remediation = ""
        };
    }

    private static CheckResult CheckAntigravityRuntimeProcesses(string shimPath)
    {
        var ps = File.Exists(PowerShellBackend.Pwsh7Path)
            ? PowerShellBackend.Pwsh7Path
            : PowerShellBackend.WindowsPowerShellPath;
        var query = @"
$antigravity = Get-CimInstance Win32_Process |
  Where-Object { $_.Name -like 'Antigravity IDE*' -or $_.CommandLine -match 'Antigravity IDE|antigravity-ide' } |
  Select-Object -ExpandProperty ProcessId
$children = Get-CimInstance Win32_Process |
  Where-Object { $_.Name -in @('powershell.exe','pwsh.exe') -and $antigravity -contains $_.ParentProcessId } |
  ForEach-Object { '{0}|{1}|{2}' -f $_.ProcessId, $_.ExecutablePath, ($_.CommandLine -replace '\r?\n',' ') }
$children
";
        var result = ProcessRunner.Run(ps, ["-NoProfile", "-Command", query]);
        if (result.ExitCode != 0)
        {
            return new CheckResult
            {
                Name = "antigravity-runtime",
                Status = "warn",
                Message = $"Unable to inspect live Antigravity shell processes: {result.Stderr.Trim()}",
                Remediation = "Run shellfix doctor again from a normal terminal."
            };
        }

        var rows = result.Stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('|', 3))
            .Where(parts => parts.Length >= 2)
            .ToList();

        if (rows.Count == 0)
        {
            return new CheckResult
            {
                Name = "antigravity-runtime",
                Status = "pass",
                Message = "No live Antigravity PowerShell child processes detected.",
                Remediation = ""
            };
        }

        var bypasses = rows
            .Where(parts => !parts[1].Equals(shimPath, StringComparison.OrdinalIgnoreCase))
            .Select(parts => $"{parts[0]}={parts[1]}")
            .ToList();

        if (bypasses.Count == 0)
        {
            return new CheckResult
            {
                Name = "antigravity-runtime",
                Status = "pass",
                Message = $"Live Antigravity PowerShell children route through Shellfix: {rows.Count}",
                Remediation = ""
            };
        }

        return new CheckResult
        {
            Name = "antigravity-runtime",
            Status = "warn",
            Message = $"Live Antigravity PowerShell children bypass Shellfix: {string.Join("; ", bypasses)}",
            Remediation = "Close stale Antigravity terminals/windows and reopen them after shellfix repair antigravity."
        };
    }

    private static string ResolveWindowsCommand(string tool)
    {
        return ResolveWindowsCommands(tool).FirstOrDefault() ?? new NativeToolResolver().Resolve(tool) ?? "";
    }

    private static List<string> ResolveWindowsCommands(string tool)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe"),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            process.StartInfo.Environment["PATH"] = BuildRefreshedPath();
            process.StartInfo.ArgumentList.Add(tool);
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            if (process.ExitCode != 0) { return []; }
            return stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(IsExecutablePath)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsExecutablePath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".com", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRefreshedPath()
    {
        var parts = new List<string>();
        foreach (var value in new[]
        {
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User)
        })
        {
            if (string.IsNullOrWhiteSpace(value)) { continue; }
            foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrWhiteSpace(part)) { continue; }
                if (!parts.Contains(part, StringComparer.OrdinalIgnoreCase))
                {
                    parts.Add(part);
                }
            }
        }

        return string.Join(';', parts);
    }
}
