namespace Shellfix.Cli;

internal sealed class CommandOptions
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    public static CommandOptions Parse(string[] args)
    {
        var options = new CommandOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                options._values[token] = "true";
                continue;
            }

            var name = token[2..];
            string? value = "true";
            var equals = name.IndexOf('=');
            if (equals >= 0)
            {
                value = name[(equals + 1)..];
                name = name[..equals];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            options._values[name] = value;
        }

        return options;
    }

    public bool Has(string name) => _values.ContainsKey(name);

    public string Get(string name, string fallback) =>
        _values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value! : fallback;
}
