namespace Shellfix.Cli;

internal sealed class ShimInstaller
{
    private readonly ShellfixContext _context;

    public ShimInstaller(ShellfixContext context)
    {
        _context = context;
    }

    public void Install(string sourceRoot, InstallState state)
    {
        Directory.CreateDirectory(state.InstallRoot);
        Directory.CreateDirectory(state.BinDir);

        var sourceShim = FindSourceShim(sourceRoot);
        if (sourceShim is null)
        {
            throw new InvalidOperationException("Cannot find powershell.exe. Build from source or place powershell.exe next to install.ps1/shellfix.exe.");
        }

        File.Copy(sourceShim, state.ProductShimPath, overwrite: true);
        File.Copy(sourceShim, state.ShimPath, overwrite: true);
        Log.Ok($"Installed shim: {state.ShimPath}");

        var sourcePdb = Path.ChangeExtension(sourceShim, ".pdb");
        if (File.Exists(sourcePdb))
        {
            File.Copy(sourcePdb, Path.Combine(state.InstallRoot, "powershell.pdb"), overwrite: true);
            File.Copy(sourcePdb, Path.Combine(state.BinDir, "powershell.pdb"), overwrite: true);
        }

        var self = _context.ExecutablePath;
        if (File.Exists(self) && !Paths.SamePath(self, state.CliPath))
        {
            File.Copy(self, state.CliPath, overwrite: true);
            CopyCliCompanionFiles(Path.GetDirectoryName(self) ?? AppContext.BaseDirectory, state.InstallRoot);
            Log.Ok($"Installed CLI: {state.CliPath}");
        }
    }

    private static void CopyCliCompanionFiles(string sourceDirectory, string installRoot)
    {
        foreach (var pattern in new[] { "shellfix.dll", "shellfix.deps.json", "shellfix.runtimeconfig.json", "shellfix.pdb" })
        {
            var source = Path.Combine(sourceDirectory, pattern);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(installRoot, pattern), overwrite: true);
            }
        }
    }

    private string? FindSourceShim(string sourceRoot)
    {
        var candidates = new[]
        {
            Path.Combine(sourceRoot, "shim", "out", "powershell.exe"),
            Path.Combine(sourceRoot, "powershell.exe"),
            Path.Combine(AppContext.BaseDirectory, "powershell.exe"),
            Path.Combine(Path.GetDirectoryName(_context.ExecutablePath) ?? AppContext.BaseDirectory, "powershell.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
