namespace Shellfix.Cli;

internal sealed class ShellfixCli
{
    private readonly ShellfixContext _context;

    public ShellfixCli(ShellfixContext context)
    {
        _context = context;
    }

    public int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return command switch
        {
            "install" => new InstallCommand(_context).Run(CommandOptions.Parse(rest)),
            "uninstall" => new UninstallCommand(_context).Run(CommandOptions.Parse(rest)),
            "status" => new StatusCommand(_context).Run(CommandOptions.Parse(rest)),
            "doctor" => new DoctorCommand(_context).Run(CommandOptions.Parse(rest)),
            "explain" => new ExplainCommand(_context).Run(rest),
            "repair" => new RepairCommand(_context).Run(rest),
            "test" => new TestCommand(_context).Run(CommandOptions.Parse(rest)),
            _ => Unknown(command)
        };
    }

    private static bool IsHelp(string value) =>
        value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("help", StringComparison.OrdinalIgnoreCase);

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"[ERROR] Unknown command: {command}");
        WriteHelp();
        return 1;
    }

    private static void WriteHelp()
    {
        Console.WriteLine("""
        shellfix - management CLI for the Shellfix PowerShell/WSL shim

        Usage:
          shellfix install [--wsl-distro Ubuntu-24.04] [--bin-dir <path>] [--skip-profile] [--skip-shortcuts] [--skip-antigravity-settings]
          shellfix uninstall
          shellfix status [--json]
          shellfix doctor [--json]
          shellfix explain [--json] "<command>"
          shellfix repair antigravity
          shellfix test [--antigravity-settings] [--shortcuts] [--incidents] [--fixture <path>]

        Compatibility:
          install.ps1 remains as a bootstrapper and forwards legacy flags to this CLI.
        """);
    }
}
