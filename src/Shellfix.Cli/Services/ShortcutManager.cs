namespace Shellfix.Cli;

internal sealed class ShortcutManager
{
    private readonly ShellfixContext _context;
    private readonly IReadOnlyList<IdeDefinition> _ides;

    public ShortcutManager(ShellfixContext context)
    {
        _context = context;
        _ides = IdeRegistry.Build(context);
    }

    public void PatchKnownIdeShortcuts(InstallState state)
    {
        Log.Step("Configuring IDE launch integration");
        foreach (var ide in _ides)
        {
            var exePath = ide.ExePaths.FirstOrDefault(File.Exists);
            if (exePath is null) { continue; }

            if (!ide.PatchShortcuts)
            {
                CleanupManagedShortcutArtifacts(ide, state);
                Log.Ok($"{ide.Name} shortcuts left unmodified; managed through IDE terminal settings.");
                continue;
            }

            var shortcuts = FindShortcuts(ide).ToList();
            if (shortcuts.Count == 0)
            {
                Log.Warn($"No shortcuts found for {ide.Name}");
                continue;
            }

            foreach (var shortcut in shortcuts)
            {
                var existingPatch = state.Shortcuts.FirstOrDefault(s => Paths.SamePath(s.ShortcutPath, shortcut) && File.Exists(s.BackupPath));
                if (existingPatch is not null && IsAlreadyPatched(shortcut, existingPatch.LauncherPath))
                {
                    var sidecar = shortcut + ".shellfix-backup";
                    if (File.Exists(sidecar))
                    {
                        existingPatch.BackupPath = Backup.Copy(_context, sidecar, "shortcuts");
                    }
                    Log.Ok($"Already patched: {Path.GetFileName(shortcut)}");
                    continue;
                }

                var patch = PatchShortcut(shortcut, state.BinDir, exePath);
                patch.IdeName = ide.Name;
                state.UpsertShortcut(patch);
            }
        }
    }

    public void Restore(InstallState state)
    {
        foreach (var patch in state.Shortcuts)
        {
            try
            {
                if (File.Exists(patch.BackupPath))
                {
                    File.Copy(patch.BackupPath, patch.ShortcutPath, overwrite: true);
                    Log.Ok($"Restored shortcut: {patch.ShortcutPath}");
                }
                if (File.Exists(patch.LauncherPath)) { File.Delete(patch.LauncherPath); }
                var sidecar = patch.ShortcutPath + ".shellfix-backup";
                if (File.Exists(sidecar)) { File.Delete(sidecar); }
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not restore shortcut {patch.ShortcutPath}: {ex.Message}");
            }
        }
    }

    public IEnumerable<CheckResult> Check(InstallState? state)
    {
        if (state is null || state.Shortcuts.Count == 0)
        {
            var patchableInstalledIde = _ides.Any(ide => ide.PatchShortcuts && ide.ExePaths.Any(File.Exists));
            yield return new CheckResult
            {
                Name = "shortcuts",
                Status = patchableInstalledIde ? "warn" : "pass",
                Message = patchableInstalledIde ? "No shortcut patches recorded in state." : "No shortcut-managed IDEs detected.",
                Remediation = patchableInstalledIde ? "Run shellfix install from the shortcuts you use, or launch through launch-ide.bat." : ""
            };
            yield break;
        }

        foreach (var patch in state.Shortcuts)
        {
            var pass = File.Exists(patch.ShortcutPath) && File.Exists(patch.BackupPath) && IsAlreadyPatched(patch.ShortcutPath, patch.LauncherPath);
            yield return new CheckResult
            {
                Name = $"shortcut:{patch.IdeName}",
                Status = pass ? "pass" : "warn",
                Message = pass ? $"Patched shortcut recorded: {patch.ShortcutPath}" : $"Shortcut patch incomplete: {patch.ShortcutPath}",
                Remediation = pass ? "" : "Run shellfix install to refresh shortcut patches."
            };
        }
    }

    private void CleanupManagedShortcutArtifacts(IdeDefinition ide, InstallState state)
    {
        foreach (var shortcut in FindShortcuts(ide))
        {
            var launcher = shortcut + ".shellfix-launcher.vbs";
            var sidecar = shortcut + ".shellfix-backup";
            if (File.Exists(launcher))
            {
                File.Delete(launcher);
                Log.Ok($"Removed stale launcher: {launcher}");
            }
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
                Log.Ok($"Removed stale shortcut backup: {sidecar}");
            }
        }

        state.Shortcuts.RemoveAll(patch => patch.IdeName.Equals(ide.Name, StringComparison.OrdinalIgnoreCase));
    }

    public void SelfTest()
    {
        var root = Temp.Create("shellfix shortcut test");
        try
        {
            var binDir = Path.Combine(root, "bin dir");
            var workDir = Path.Combine(root, "work dir");
            Directory.CreateDirectory(binDir);
            Directory.CreateDirectory(workDir);
            var shortcutPath = Path.Combine(root, "IDE Shortcut.lnk");
            var outputPath = Path.Combine(root, "shortcut-output.txt");
            var scriptPath = Path.Combine(root, "capture.ps1");
            File.WriteAllText(scriptPath, """
            param([string]$OutputPath)
            Set-Content -LiteralPath $OutputPath -Value $env:PATH.Split(';')[0] -Encoding UTF8
            """, Utf8.NoBom);

            var ps = PowerShellBackend.WindowsPowerShellPath;
            var shell = Com.CreateWScriptShell();
            var lnk = shell.CreateShortcut(shortcutPath);
            lnk.TargetPath = ps;
            lnk.Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -OutputPath \"{outputPath}\"";
            lnk.WorkingDirectory = workDir;
            lnk.Save();

            var patch = PatchShortcut(shortcutPath, binDir, ps);
            if (!File.Exists(patch.BackupPath)) { throw new InvalidOperationException("Backup not created."); }
            Restore(new InstallState { Shortcuts = [patch] });
            if (!File.Exists(shortcutPath)) { throw new InvalidOperationException("Shortcut not restored."); }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private ShortcutPatchState PatchShortcut(string shortcutPath, string binDir, string exePath)
    {
        var shell = Com.CreateWScriptShell();
        var lnk = shell.CreateShortcut(shortcutPath);
        var launcherPath = shortcutPath + ".shellfix-launcher.vbs";
        var sidecar = shortcutPath + ".shellfix-backup";
        if (!File.Exists(sidecar)) { File.Copy(shortcutPath, sidecar, overwrite: true); }
        var backupPath = Backup.Copy(_context, sidecar, "shortcuts");

        var origArgs = (string?)lnk.Arguments ?? "";
        var origWorkDir = string.IsNullOrWhiteSpace((string?)lnk.WorkingDirectory) ? Path.GetDirectoryName(exePath) ?? "" : (string)lnk.WorkingDirectory;
        WriteLauncher(launcherPath, binDir, exePath, origArgs, origWorkDir);

        lnk.TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "wscript.exe");
        lnk.Arguments = $"\"{launcherPath}\"";
        lnk.WorkingDirectory = Path.GetDirectoryName(launcherPath) ?? "";
        lnk.WindowStyle = 7;
        lnk.Save();

        Log.Ok($"Patched shortcut: {Path.GetFileName(shortcutPath)}");
        return new ShortcutPatchState { ShortcutPath = shortcutPath, BackupPath = backupPath, LauncherPath = launcherPath };
    }

    private static bool IsAlreadyPatched(string shortcutPath, string launcherPath)
    {
        if (!File.Exists(shortcutPath) || string.IsNullOrWhiteSpace(launcherPath)) { return false; }
        try
        {
            var shell = Com.CreateWScriptShell();
            var lnk = shell.CreateShortcut(shortcutPath);
            return ((string)lnk.TargetPath).EndsWith("wscript.exe", StringComparison.OrdinalIgnoreCase) &&
                ((string)lnk.Arguments).Contains(launcherPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private IEnumerable<string> FindShortcuts(IdeDefinition ide)
    {
        var roots = new[]
        {
            Path.Combine(_context.UserProfile, "Desktop"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)),
            Path.Combine(_context.AppData, "Microsoft", "Windows", "Start Menu", "Programs")
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var name in ide.ShortcutNames)
            {
                foreach (var file in Directory.EnumerateFiles(root, name, SearchOption.AllDirectories))
                {
                    yield return file;
                }
            }
        }
    }

    private static void WriteLauncher(string path, string binDir, string exePath, string arguments, string workingDirectory)
    {
        var script = $"""
        Option Explicit
        Dim shell, env, app, currentPath
        Set shell = CreateObject("WScript.Shell")
        Set env = shell.Environment("PROCESS")
        currentPath = env("PATH")
        env("PATH") = {Vbs.StringLiteral(binDir)} & ";" & currentPath
        Set app = CreateObject("Shell.Application")
        app.ShellExecute {Vbs.StringLiteral(exePath)}, {Vbs.StringLiteral(arguments)}, {Vbs.StringLiteral(workingDirectory)}, "open", 1
        """;
        File.WriteAllText(path, script, Encoding.Unicode);
    }
}
