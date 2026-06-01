namespace Shellfix.Cli;

internal sealed class GuardCommand
{
    public int Run(string[] args)
    {
        if (args.Length == 0 || !args[0].Equals("antigravity-run-command", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("[ERROR] Supported guard target: antigravity-run-command");
            return 1;
        }

        var options = CommandOptions.Parse(args.Skip(1).ToArray());
        var command = options.Get("command", "");
        if (string.IsNullOrWhiteSpace(command))
        {
            var input = Console.In.ReadToEnd();
            command = AntigravityRunCommandGuard.ExtractCommandLine(input);
        }

        var decision = AntigravityRunCommandGuard.Evaluate(command);
        var jsonOptions = new JsonSerializerOptions(Json.Options)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        Console.WriteLine(JsonSerializer.Serialize(decision, jsonOptions));
        return decision.Decision.Equals("deny", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
    }
}
