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
        var settingsPath = Path.Combine(_context.AppData, "Antigravity IDE", "User", "settings.json");
        if (!File.Exists(settingsPath))
        {
            Log.Warn($"Antigravity IDE settings not found: {settingsPath}");
            return;
        }

        var patch = state.AntigravitySettings;
        if (patch is null || string.IsNullOrWhiteSpace(patch.BackupPath) || !File.Exists(patch.BackupPath))
        {
            patch = new SettingsPatchState { SettingsPath = settingsPath, BackupPath = Backup.Copy(_context, settingsPath, "settings") };
            state.AntigravitySettings = patch;
        }

        UpdateSettings(settingsPath, state.ShimPath);
        Log.Ok($"Antigravity IDE settings merged: {settingsPath}");
    }

    public void Restore(InstallState state)
    {
        var patch = state.AntigravitySettings;
        if (patch is null || !File.Exists(patch.BackupPath)) { return; }
        Directory.CreateDirectory(Path.GetDirectoryName(patch.SettingsPath)!);
        File.Copy(patch.BackupPath, patch.SettingsPath, overwrite: true);
        Log.Ok($"Restored Antigravity settings: {patch.SettingsPath}");
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
            if (!second.Contains(Jsonc.StringLiteral(shimPath), StringComparison.Ordinal)) { throw new InvalidOperationException("Shim path was not written."); }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static CheckResult Check(ShellfixContext context, InstallState? state)
    {
        var settingsPath = state?.AntigravitySettings?.SettingsPath ?? Path.Combine(context.AppData, "Antigravity IDE", "User", "settings.json");
        if (!File.Exists(settingsPath))
        {
            return new CheckResult { Name = "antigravity", Status = "warn", Message = $"Settings not found: {settingsPath}", Remediation = "Install or launch Antigravity IDE once, then run shellfix repair antigravity." };
        }

        var content = File.ReadAllText(settingsPath, Utf8.NoBom);
        var hasAgent = Regex.IsMatch(content, @"""terminal\.integrated\.agentHostProfile\.windows""\s*:\s*""shellfix""");
        var hasDefault = Regex.IsMatch(content, @"""terminal\.integrated\.defaultProfile\.windows""\s*:\s*""shellfix""");
        var hasAutomation = Regex.IsMatch(content, @"""terminal\.integrated\.automationProfile\.windows""[\s\S]*powershell\.exe");
        var pass = hasAgent && hasDefault && hasAutomation;
        return new CheckResult
        {
            Name = "antigravity",
            Status = pass ? "pass" : "fail",
            Message = pass ? "Agent, automation, and default terminal settings route through shellfix." : "Antigravity settings are incomplete.",
            Remediation = "Run shellfix repair antigravity."
        };
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
