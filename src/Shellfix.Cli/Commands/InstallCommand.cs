namespace Shellfix.Cli;

internal sealed class InstallCommand
{
    private readonly ShellfixContext _context;

    public InstallCommand(ShellfixContext context)
    {
        _context = context;
    }

    public int Run(CommandOptions options)
    {
        var sourceRoot = options.Get("source-root", _context.RepoRoot);
        var binDir = options.Get("bin-dir", _context.DefaultBinDir);
        var wslDistro = options.Get("wsl-distro", "Ubuntu-24.04");

        Log.Step("Pre-flight checks");
        WslManager.Validate(wslDistro);
        PowerShellPolicy.EnsureRemoteSigned();
        WindowsRegistry.TryEnableLongPaths();

        var stateStore = new StateStore(_context);
        var previousState = stateStore.Load();
        var state = previousState ?? InstallState.Create(_context, binDir, wslDistro);
        state.Version = _context.Version;
        state.InstalledAtUtc = DateTimeOffset.UtcNow.ToString("O");
        state.InstallRoot = _context.ProgramFilesShellfix;
        state.BinDir = binDir;
        state.CliPath = Path.Combine(state.InstallRoot, "shellfix.exe");
        state.ShimPath = Path.Combine(binDir, "powershell.exe");
        state.ProductShimPath = Path.Combine(state.InstallRoot, "powershell.exe");
        state.WslDistro = wslDistro;
        Backup.PreferOldestRecordedBackups(_context, state);

        Log.Step($"Installing Shellfix to {state.InstallRoot}");
        Directory.CreateDirectory(state.InstallRoot);
        Directory.CreateDirectory(_context.BackupRoot);
        new ShimInstaller(_context).Install(sourceRoot, state);

        var pathManager = new PathManager();
        if (string.IsNullOrWhiteSpace(state.OriginalUserPath))
        {
            state.OriginalUserPath = pathManager.GetUserPath();
        }
        var addedInstallRoot = pathManager.EnsurePrepended(state.InstallRoot);
        var addedBin = pathManager.EnsurePrepended(state.BinDir);
        state.AddedInstallRootToPath = state.AddedInstallRootToPath || addedInstallRoot || previousState is not null;
        state.AddedBinToPath = state.AddedBinToPath || addedBin;

        Environment.SetEnvironmentVariable("SHELLFIX_WSL_DISTRO", wslDistro, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("SHELLFIX_WSL_DISTRO", wslDistro);
        Log.Ok($"Configured WSL distro: {wslDistro}");

        if (!options.Has("skip-profile"))
        {
            new ProfileInstaller(_context).Install(sourceRoot, state);
        }

        if (!options.Has("skip-shortcuts"))
        {
            new ShortcutManager(_context).PatchKnownIdeShortcuts(state);
        }

        if (!options.Has("skip-antigravity-settings"))
        {
            new AntigravitySettingsManager(_context).InstallOrRepair(state);
        }

        stateStore.Save(state);

        Log.Success("Shellfix installation complete");
        Console.WriteLine($"  CLI:  {state.CliPath}");
        Console.WriteLine($"  Shim: {state.ShimPath}");
        Console.WriteLine("  Verify: shellfix doctor");
        return 0;
    }
}
