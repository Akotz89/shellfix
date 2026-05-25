namespace Shellfix.Cli;

internal sealed class ProfileInstaller
{
    private const string BeginMarker = "# >>> shellfix >>>";
    private const string EndMarker = "# <<< shellfix <<<";
    private readonly ShellfixContext _context;

    public ProfileInstaller(ShellfixContext context)
    {
        _context = context;
    }

    public void Install(string sourceRoot, InstallState state)
    {
        Log.Step("Installing PowerShell profile integration");
        var sourceProfile = FindSourceProfile(sourceRoot);
        if (sourceProfile is null)
        {
            Log.Warn("Profile source not found; skipping profile installation.");
            return;
        }

        var profileDir = Path.Combine(_context.UserProfile, "Documents", "WindowsPowerShell");
        var profilePath = Path.Combine(profileDir, "Microsoft.PowerShell_profile.ps1");
        var snippetPath = Path.Combine(profileDir, "shellfix_profile.ps1");
        Directory.CreateDirectory(profileDir);

        var backup = state.Profile?.BackupPath;
        if (string.IsNullOrWhiteSpace(backup) || !File.Exists(backup))
        {
            backup = File.Exists(profilePath) ? Backup.Copy(_context, profilePath, "profile") : "";
        }
        File.Copy(sourceProfile, snippetPath, overwrite: true);

        var escapedSnippetPath = snippetPath.Replace("'", "''");
        var block = string.Join(Environment.NewLine,
            BeginMarker,
            "# shellfix - managed by shellfix.exe. Run \"shellfix uninstall\" to remove.",
            $"if (Test-Path '{escapedSnippetPath}') {{ . '{escapedSnippetPath}' }}",
            EndMarker);

        var existing = File.Exists(profilePath) ? File.ReadAllText(profilePath, Utf8.NoBom) : "";
        string next;
        if (existing.Contains(BeginMarker, StringComparison.Ordinal))
        {
            next = Regex.Replace(existing, $"{Regex.Escape(BeginMarker)}.*?{Regex.Escape(EndMarker)}", block, RegexOptions.Singleline);
        }
        else
        {
            next = existing.TrimEnd() + Environment.NewLine + block + Environment.NewLine;
        }

        File.WriteAllText(profilePath, next.TrimStart().TrimEnd() + Environment.NewLine, Utf8.NoBom);
        state.Profile = new ProfilePatchState { ProfilePath = profilePath, SnippetPath = snippetPath, BackupPath = backup };
        Log.Ok($"Profile configured: {profilePath}");
    }

    public void Uninstall(InstallState state)
    {
        var profile = state.Profile;
        if (profile is null)
        {
            profile = new ProfilePatchState
            {
                ProfilePath = Path.Combine(_context.UserProfile, "Documents", "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1"),
                SnippetPath = Path.Combine(_context.UserProfile, "Documents", "WindowsPowerShell", "shellfix_profile.ps1")
            };
        }

        if (File.Exists(profile.ProfilePath))
        {
            var content = File.ReadAllText(profile.ProfilePath, Utf8.NoBom);
            var next = Regex.Replace(content, $@"\r?\n?{Regex.Escape(BeginMarker)}.*?{Regex.Escape(EndMarker)}\r?\n?", Environment.NewLine, RegexOptions.Singleline).Trim();
            File.WriteAllText(profile.ProfilePath, next.Length == 0 ? "" : next + Environment.NewLine, Utf8.NoBom);
            Log.Ok($"Removed profile block: {profile.ProfilePath}");
        }

        if (File.Exists(profile.SnippetPath))
        {
            File.Delete(profile.SnippetPath);
            Log.Ok($"Removed profile snippet: {profile.SnippetPath}");
        }
    }

    public static CheckResult Check(ShellfixContext context, InstallState? state)
    {
        var path = state?.Profile?.ProfilePath ?? Path.Combine(context.UserProfile, "Documents", "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");
        var pass = File.Exists(path) && File.ReadAllText(path, Utf8.NoBom).Contains(BeginMarker, StringComparison.Ordinal);
        return new CheckResult
        {
            Name = "profile",
            Status = pass ? "pass" : "warn",
            Message = pass ? $"Profile block present: {path}" : $"Profile block not found: {path}",
            Remediation = pass ? "" : "Run shellfix install without --skip-profile."
        };
    }

    public static void SelfTest(ShellfixContext context)
    {
        var root = Temp.Create("shellfix profile test");
        try
        {
            var sourceRoot = Path.Combine(root, "source");
            var profileSourceDir = Path.Combine(sourceRoot, "profile");
            Directory.CreateDirectory(profileSourceDir);
            File.WriteAllText(Path.Combine(profileSourceDir, "Microsoft.PowerShell_profile.ps1"), "Set-StrictMode -Version Latest" + Environment.NewLine, Utf8.NoBom);

            var fakeContext = new ShellfixContextForTests(context, root);
            var state = InstallState.Create(fakeContext, Path.Combine(root, "bin"), "Ubuntu-24.04");
            var installer = new ProfileInstaller(fakeContext);
            installer.Install(sourceRoot, state);
            var first = File.ReadAllText(state.Profile!.ProfilePath, Utf8.NoBom);
            installer.Install(sourceRoot, state);
            var second = File.ReadAllText(state.Profile!.ProfilePath, Utf8.NoBom);
            if (first != second) { throw new InvalidOperationException("Profile install is not idempotent."); }
            installer.Uninstall(state);
            if (File.Exists(state.Profile.SnippetPath)) { throw new InvalidOperationException("Snippet was not removed."); }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string? FindSourceProfile(string sourceRoot)
    {
        var candidates = new[]
        {
            Path.Combine(sourceRoot, "profile", "Microsoft.PowerShell_profile.ps1"),
            Path.Combine(sourceRoot, "Microsoft.PowerShell_profile.ps1"),
            Path.Combine(AppContext.BaseDirectory, "profile", "Microsoft.PowerShell_profile.ps1"),
            Path.Combine(AppContext.BaseDirectory, "Microsoft.PowerShell_profile.ps1")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
