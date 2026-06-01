namespace Shellfix.Cli;

internal sealed class InstallState
{
    public string Version { get; set; } = "";
    public string InstalledAtUtc { get; set; } = "";
    public string InstallRoot { get; set; } = "";
    public string BinDir { get; set; } = "";
    public string CliPath { get; set; } = "";
    public string ShimPath { get; set; } = "";
    public string ProductShimPath { get; set; } = "";
    public string WslDistro { get; set; } = "";
    public string OriginalUserPath { get; set; } = "";
    public bool AddedInstallRootToPath { get; set; }
    public bool AddedBinToPath { get; set; }
    public ProfilePatchState? Profile { get; set; }
    public SettingsPatchState? AntigravitySettings { get; set; }
    public List<SettingsPatchState> AntigravitySettingsPatches { get; set; } = [];
    public List<ShortcutPatchState> Shortcuts { get; set; } = [];

    public static InstallState Create(ShellfixContext context, string binDir, string wslDistro) =>
        new()
        {
            Version = context.Version,
            InstalledAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            InstallRoot = context.ProgramFilesShellfix,
            BinDir = binDir,
            CliPath = Path.Combine(context.ProgramFilesShellfix, "shellfix.exe"),
            ShimPath = Path.Combine(binDir, "powershell.exe"),
            ProductShimPath = Path.Combine(context.ProgramFilesShellfix, "powershell.exe"),
            WslDistro = wslDistro
        };

    public void UpsertShortcut(ShortcutPatchState patch)
    {
        Shortcuts.RemoveAll(s => Paths.SamePath(s.ShortcutPath, patch.ShortcutPath));
        Shortcuts.Add(patch);
    }
}
