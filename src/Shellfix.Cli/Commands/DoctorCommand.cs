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
        checks.Add(WslManager.Check(wslDistro));
        checks.Add(CheckNativeToolRouting());
        checks.Add(CheckDirectNativeRouting());
        checks.Add(new PathManager().Check(shimPath, installRoot, state?.BinDir ?? context.DefaultBinDir));
        checks.Add(ProfileInstaller.Check(context, state));
        checks.AddRange(new ShortcutManager(context).Check(state));
        checks.Add(AntigravitySettingsManager.Check(context, state));

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
        var tools = new[] { "python", "python3", "node", "npx", "d2" };
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

    private static CheckResult CheckDirectNativeRouting()
    {
        var d2 = ResolveWindowsCommand("d2");
        if (string.IsNullOrWhiteSpace(d2))
        {
            return new CheckResult
            {
                Name = "native-direct",
                Status = "warn",
                Message = "Full-path native routing is enabled; d2 was not found for noisy-stderr verification.",
                Remediation = "Install D2 if Antigravity needs diagram rendering, or use shellfix explain to inspect another command."
            };
        }

        var shadowed = ResolveWindowsCommands("d2");
        var shadowNote = shadowed.Count > 1 ? $" candidates={string.Join(" | ", shadowed)}" : $" path={d2}";
        return new CheckResult
        {
            Name = "native-direct",
            Status = "pass",
            Message = $"Full-path native routing enabled for known developer tools; noisy-stderr tool d2{shadowNote}",
            Remediation = ""
        };
    }

    private static string ResolveWindowsCommand(string tool)
    {
        return ResolveWindowsCommands(tool).FirstOrDefault() ?? "";
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
            return stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        catch
        {
            return [];
        }
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
