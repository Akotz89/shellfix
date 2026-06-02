namespace Shellfix.Cli;

internal sealed class AntigravitySettingsManager
{
    private readonly ShellfixContext _context;

    public AntigravitySettingsManager(ShellfixContext context)
    {
        _context = context;
    }

    public void InstallOrRepair(InstallState state)
    {
        var settingsPaths = FindSettingsPaths().ToList();
        if (settingsPaths.Count == 0)
        {
            Log.Warn($"Antigravity settings not found under: {Path.Combine(_context.AppData, "Antigravity IDE")} or {Path.Combine(_context.AppData, "Antigravity")}");
            return;
        }

        foreach (var settingsPath in settingsPaths)
        {
            var patch = FindPatch(state, settingsPath);
            if (patch is null || string.IsNullOrWhiteSpace(patch.BackupPath) || !File.Exists(patch.BackupPath))
            {
                patch = new SettingsPatchState { SettingsPath = settingsPath, BackupPath = Backup.Copy(_context, settingsPath, "settings") };
                state.AntigravitySettingsPatches.RemoveAll(p => Paths.SamePath(p.SettingsPath, settingsPath));
                state.AntigravitySettingsPatches.Add(patch);
                state.AntigravitySettings ??= patch;
            }

            UpdateSettings(settingsPath, state.ShimPath);
            Log.Ok($"Antigravity settings merged: {settingsPath}");
        }
    }

    public void Restore(InstallState state)
    {
        var patches = state.AntigravitySettingsPatches.Count > 0
            ? state.AntigravitySettingsPatches
            : state.AntigravitySettings is null ? [] : [state.AntigravitySettings];

        foreach (var patch in patches)
        {
            if (!File.Exists(patch.BackupPath)) { continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(patch.SettingsPath)!);
            File.Copy(patch.BackupPath, patch.SettingsPath, overwrite: true);
            Log.Ok($"Restored Antigravity settings: {patch.SettingsPath}");
        }
    }

    public void SelfTest()
    {
        var root = Temp.Create("shellfix antigravity settings");
        try
        {
            var settingsPath = Path.Combine(root, "settings.json");
            var shimPath = Path.Combine(root, "bin", "powershell.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(shimPath)!);
            File.WriteAllText(settingsPath, """
            {
              "workbench.colorTheme": "Test Theme",
              "terminal.integrated.defaultProfile.windows": "PowerShell",
              "terminal.integrated.windowsEnableConpty": false,
              "terminal.integrated.profiles.windows": {
                "PowerShell": {
                  "source": "PowerShell"
                },
              },
            }
            """, Utf8.NoBom);

            UpdateSettings(settingsPath, shimPath);
            var first = File.ReadAllText(settingsPath, Utf8.NoBom);
            UpdateSettings(settingsPath, shimPath);
            var second = File.ReadAllText(settingsPath, Utf8.NoBom);
            if (first != second) { throw new InvalidOperationException("Settings merge is not idempotent."); }
            if (!second.Contains("\"workbench.colorTheme\": \"Test Theme\"", StringComparison.Ordinal)) { throw new InvalidOperationException("Existing settings were not preserved."); }
            if (!second.Contains("\"terminal.integrated.defaultProfile.windows\": \"shellfix\"", StringComparison.Ordinal)) { throw new InvalidOperationException("Default profile was not set."); }
            if (!second.Contains("\"terminal.integrated.agentHostProfile.windows\": \"shellfix\"", StringComparison.Ordinal)) { throw new InvalidOperationException("Agent host profile was not set."); }
            if (!second.Contains("\"terminal.integrated.windowsEnableConpty\": true", StringComparison.Ordinal)) { throw new InvalidOperationException("ConPTY was not enabled for the shellfix terminal profile."); }
            if (!second.Contains(Jsonc.StringLiteral(shimPath), StringComparison.Ordinal)) { throw new InvalidOperationException("Shim path was not written."); }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static CheckResult Check(ShellfixContext context, InstallState? state)
    {
        var settingsPaths = FindSettingsPaths(context).ToList();
        if (settingsPaths.Count == 0)
        {
            return new CheckResult { Name = "antigravity", Status = "warn", Message = "Antigravity settings not found.", Remediation = "Install or launch Antigravity once, then run shellfix repair antigravity." };
        }

        var incomplete = settingsPaths.Where(path => !IsPatched(path, out _)).ToList();
        var conptyDisabled = settingsPaths.Where(path => IsPatched(path, out var disabled) && disabled).ToList();
        var pass = incomplete.Count == 0 && conptyDisabled.Count == 0;
        return new CheckResult
        {
            Name = "antigravity",
            Status = pass ? "pass" : "fail",
            Message = pass
                ? $"Agent, automation, and default terminal settings route through shellfix with ConPTY enabled in {settingsPaths.Count} settings file(s)."
                : conptyDisabled.Count > 0
                    ? $"Antigravity routes through shellfix, but legacy ConPTY-disabled terminal mode is still set: {string.Join("; ", conptyDisabled)}"
                    : $"Antigravity settings are incomplete: {string.Join("; ", incomplete)}",
            Remediation = "Run shellfix repair antigravity."
        };
    }

    private IEnumerable<string> FindSettingsPaths() => FindSettingsPaths(_context);

    private static IEnumerable<string> FindSettingsPaths(ShellfixContext context)
    {
        var names = new[] { "Antigravity IDE", "Antigravity" };
        return names
            .Select(name => Path.Combine(context.AppData, name, "User", "settings.json"))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static SettingsPatchState? FindPatch(InstallState state, string settingsPath)
    {
        var patch = state.AntigravitySettingsPatches.FirstOrDefault(p => Paths.SamePath(p.SettingsPath, settingsPath));
        if (patch is not null) { return patch; }
        return state.AntigravitySettings is not null && Paths.SamePath(state.AntigravitySettings.SettingsPath, settingsPath)
            ? state.AntigravitySettings
            : null;
    }

    private static bool IsPatched(string settingsPath, out bool conptyDisabled)
    {
        var content = File.ReadAllText(settingsPath, Utf8.NoBom);
        var hasAgent = Regex.IsMatch(content, @"""terminal\.integrated\.agentHostProfile\.windows""\s*:\s*""shellfix""");
        var hasDefault = Regex.IsMatch(content, @"""terminal\.integrated\.defaultProfile\.windows""\s*:\s*""shellfix""");
        var hasAutomation = Regex.IsMatch(content, @"""terminal\.integrated\.automationProfile\.windows""[\s\S]*powershell\.exe");
        conptyDisabled = Regex.IsMatch(content, @"""terminal\.integrated\.windowsEnableConpty""\s*:\s*false");
        return hasAgent && hasDefault && hasAutomation;
    }

    private static void UpdateSettings(string settingsPath, string shimPath)
    {
        var dir = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(dir)) { Directory.CreateDirectory(dir); }
        var content = File.Exists(settingsPath) ? File.ReadAllText(settingsPath, Utf8.NoBom) : "{}";
        if (string.IsNullOrWhiteSpace(content)) { content = "{}"; }

        var shimLiteral = Jsonc.StringLiteral(shimPath);
        var shellfixProfile = $$"""
        {
              "path": {{shimLiteral}},
              "args": ["-NoLogo"],
              "icon": "terminal-powershell"
            }
        """;
        var automationProfile = $$"""
        {
            "path": {{shimLiteral}},
            "args": ["-NoLogo"]
          }
        """;

        content = Jsonc.SetObjectProperty(content, "terminal.integrated.agentHostProfile.windows", Jsonc.StringLiteral("shellfix"));
        content = Jsonc.SetObjectProperty(content, "terminal.integrated.defaultProfile.windows", Jsonc.StringLiteral("shellfix"));
        content = Jsonc.SetObjectProperty(content, "terminal.integrated.automationProfile.windows", automationProfile.Trim());
        content = Jsonc.SetObjectProperty(content, "terminal.integrated.windowsEnableConpty", "true");

        var profiles = Jsonc.FindPropertyValueRange(content, "terminal.integrated.profiles.windows");
        if (profiles is null)
        {
            content = Jsonc.SetObjectProperty(content, "terminal.integrated.profiles.windows", "{\r\n  \"shellfix\": " + shellfixProfile.Trim() + "\r\n}");
        }
        else
        {
            var profileText = content[profiles.ValueStart..profiles.ValueEnd];
            if (!profileText.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("terminal.integrated.profiles.windows exists but is not an object.");
            }

            var nextProfileText = Jsonc.SetObjectProperty(profileText, "shellfix", shellfixProfile.Trim());
            content = content[..profiles.ValueStart] + nextProfileText + content[profiles.ValueEnd..];
        }

        File.WriteAllText(settingsPath, content.TrimEnd() + "\r\n", Utf8.NoBom);
    }
}
