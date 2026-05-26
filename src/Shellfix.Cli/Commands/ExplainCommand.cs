namespace Shellfix.Cli;

internal sealed class ExplainCommand
{
    private readonly ShellfixContext _context;

    public ExplainCommand(ShellfixContext context)
    {
        _context = context;
    }

    public int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("[ERROR] Usage: shellfix explain [--json] \"<command>\"");
            return 1;
        }

        var json = args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase));
        var command = string.Join(" ", args.Where(a => !a.Equals("--json", StringComparison.OrdinalIgnoreCase))).Trim();
        var route = new CommandRouter().Classify(command);
        var target = string.IsNullOrWhiteSpace(route.Target) && route.Route == "powershell-file"
            ? PowerShellBackend.Describe()
            : route.Target;

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                version = _context.Version,
                backend = PowerShellBackend.Describe(),
                route = route.Route,
                reason = route.Reason,
                target,
                powerShellParsesPayload = route.PowerShellParsesPayload,
                confidence = route.Confidence,
                riskFlags = route.RiskFlags,
                tool = route.Tool,
                scriptExtension = route.ScriptExtension,
                argumentCount = route.Arguments?.Count ?? 0,
                topLevelShell = route.TopLevelShell,
                operatorOwner = route.OperatorOwner,
                wrapperUnwrapped = route.WrapperUnwrapped,
                blockedReason = route.BlockedReason,
                routedCommand = route.RoutedCommand
            }, Json.Options));
            return 0;
        }

        Console.WriteLine("Shellfix command explanation");
        Console.WriteLine($"Version: {_context.Version}");
        Console.WriteLine($"Backend: {PowerShellBackend.Describe()}");
        Console.WriteLine($"Route: {route.Route}");
        Console.WriteLine($"Reason: {route.Reason}");
        if (!string.IsNullOrWhiteSpace(target))
        {
            Console.WriteLine($"Target: {target}");
        }
        Console.WriteLine($"PowerShell parses payload: {(route.PowerShellParsesPayload ? "yes" : "no")}");
        Console.WriteLine($"Confidence: {route.Confidence}");
        Console.WriteLine($"Top-level shell: {route.TopLevelShell}");
        Console.WriteLine($"Operator owner: {route.OperatorOwner}");
        Console.WriteLine($"Wrapper unwrapped: {(route.WrapperUnwrapped ? "yes" : "no")}");
        if (!string.IsNullOrWhiteSpace(route.BlockedReason))
        {
            Console.WriteLine($"Blocked reason: {route.BlockedReason}");
        }
        Console.WriteLine($"Risk flags: {(route.RiskFlags.Count == 0 ? "none" : string.Join(", ", route.RiskFlags))}");
        return 0;
    }
}
