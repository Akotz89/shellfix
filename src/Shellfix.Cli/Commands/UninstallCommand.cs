namespace Shellfix.Cli;

internal sealed class UninstallCommand
{
    private readonly ShellfixContext _context;

    public UninstallCommand(ShellfixContext context)
    {
        _context = context;
    }

    public int Run(CommandOptions options)
    {
        var stateStore = new StateStore(_context);
        var state = stateStore.Load();
        if (state is null)
        {
            Log.Warn($"No install state found at {_context.StatePath}; using best-effort cleanup.");
            state = InstallState.Create(_context, _context.DefaultBinDir, Environment.GetEnvironmentVariable("SHELLFIX_WSL_DISTRO") ?? "Ubuntu-24.04");
        }

        Log.Step("Restoring settings and shortcuts");
        new AntigravitySettingsManager(_context).Restore(state);
        new ShortcutManager(_context).Restore(state);
        new ProfileInstaller(_context).Uninstall(state);

        Log.Step("Removing installed binaries");
        TryDelete(Path.Combine(state.BinDir, "powershell.exe"));
        TryDelete(Path.Combine(state.BinDir, "powershell.pdb"));
        TryDelete(Path.Combine(state.InstallRoot, "powershell.exe"));
        TryDelete(Path.Combine(state.InstallRoot, "powershell.pdb"));
        TryDelete(Path.Combine(state.InstallRoot, "shellfix.exe"));

        var pathEntriesToRemove = new List<string>();
        if (state.AddedInstallRootToPath) { pathEntriesToRemove.Add(state.InstallRoot); }
        if (state.AddedBinToPath) { pathEntriesToRemove.Add(state.BinDir); }
        if (pathEntriesToRemove.Count > 0)
        {
            new PathManager().RemoveEntries(pathEntriesToRemove.ToArray());
        }
        Environment.SetEnvironmentVariable("SHELLFIX_WSL_DISTRO", null, EnvironmentVariableTarget.User);

        stateStore.Delete();
        Log.Success("Shellfix uninstalled. Restart IDEs and terminals to clear inherited environment.");
        return 0;
    }

    private static void TryDelete(string path)
    {
        if (!File.Exists(path)) { return; }
        try
        {
            File.Delete(path);
            Log.Ok($"Removed: {path}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not remove {path}: {ex.Message}");
        }
    }
}
