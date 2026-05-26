namespace Shellfix.Cli;

internal sealed class StatusCommand
{
    private readonly ShellfixContext _context;

    public StatusCommand(ShellfixContext context)
    {
        _context = context;
    }

    public int Run(CommandOptions options)
    {
        var report = DoctorCommand.BuildReport(_context);
        if (options.Has("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(report, Json.Options));
            return 0;
        }

        Console.WriteLine($"Shellfix {report.Version}");
        Console.WriteLine($"Install root: {report.InstallRoot}");
        Console.WriteLine($"State file:   {report.StatePath}");
        Console.WriteLine($"Shim path:    {report.ShimPath}");
        Console.WriteLine($"Backend:      {report.PowerShellBackend}");
        Console.WriteLine($"WSL distro:   {report.WslDistro}");
        Console.WriteLine();
        DoctorCommand.WriteChecks(report.Checks);
        return 0;
    }
}
