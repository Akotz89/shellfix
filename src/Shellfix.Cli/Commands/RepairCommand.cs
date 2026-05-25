namespace Shellfix.Cli;

internal sealed class RepairCommand
{
    private readonly ShellfixContext _context;

    public RepairCommand(ShellfixContext context)
    {
        _context = context;
    }

    public int Run(string[] args)
    {
        if (args.Length == 0 || !args[0].Equals("antigravity", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("[ERROR] Supported repair target: antigravity");
            return 1;
        }

        var store = new StateStore(_context);
        var state = store.Load() ?? InstallState.Create(_context, _context.DefaultBinDir, Environment.GetEnvironmentVariable("SHELLFIX_WSL_DISTRO") ?? "Ubuntu-24.04");
        new AntigravitySettingsManager(_context).InstallOrRepair(state);
        store.Save(state);
        Log.Success("Antigravity settings repaired.");
        return 0;
    }
}
