namespace Shellfix.Cli;

internal class ShellfixContext
{
    public virtual string UserProfile { get; protected init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public virtual string LocalAppData { get; protected init; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public virtual string AppData { get; protected init; } = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public virtual string ProgramFilesShellfix => Path.Combine(LocalAppData, "Programs", "Shellfix");
    public virtual string DataRoot => Path.Combine(LocalAppData, "Shellfix");
    public virtual string StatePath => Path.Combine(DataRoot, "state.json");
    public virtual string BackupRoot => Path.Combine(DataRoot, "backups");
    public virtual string DefaultBinDir => Path.Combine(UserProfile, "bin");
    public virtual string RepoRoot { get; protected init; }
    public virtual string ExecutablePath { get; protected init; }
    public virtual string Version { get; protected init; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ??
        "0.0.0";

    public ShellfixContext()
    {
        ExecutablePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "shellfix.exe");
        RepoRoot = FindRepoRoot(Directory.GetCurrentDirectory()) ??
            FindRepoRoot(AppContext.BaseDirectory) ??
            AppContext.BaseDirectory;
    }

    private static string? FindRepoRoot(string start)
    {
        var dir = Directory.Exists(start) ? new DirectoryInfo(start) : new FileInfo(start).Directory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "shim", "PowerShellShim.csproj")) &&
                File.Exists(Path.Combine(dir.FullName, "profile", "Microsoft.PowerShell_profile.ps1")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
