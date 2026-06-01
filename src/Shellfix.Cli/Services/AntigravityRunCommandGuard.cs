namespace Shellfix.Cli;

internal static class AntigravityRunCommandGuard
{
    private static readonly string[] FragileTokens =
    [
        "$",
        "`",
        "\n",
        "; do",
        "for ",
        "while ",
        "<<"
    ];

    public static GuardDecision Evaluate(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return GuardDecision.Allow();
        }

        if (!IsInlineWslBash(command))
        {
            return GuardDecision.Allow();
        }

        if (!FragileTokens.Any(token => command.Contains(token, StringComparison.Ordinal)))
        {
            return GuardDecision.Allow();
        }

        return GuardDecision.Deny(
            "BLOCKED: Fragile inline WSL bash command. Antigravity run_command can route through Windows PowerShell before Shellfix receives the payload, which can expand bash variables such as $f before WSL receives them. Write the bash body to a scratch .sh file and run `wsl -d Ubuntu-24.04 -- bash /mnt/c/.../script.sh`, or call a checked-in script.");
    }

    public static string ExtractCommandLine(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return TryGetString(root, "toolCall", "args", "CommandLine")
                ?? TryGetString(root, "toolCall", "args", "commandLine")
                ?? TryGetString(root, "toolCall", "args", "command")
                ?? TryGetString(root, "args", "CommandLine")
                ?? TryGetString(root, "args", "commandLine")
                ?? TryGetString(root, "CommandLine")
                ?? TryGetString(root, "commandLine")
                ?? TryGetString(root, "command")
                ?? "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    public static void SelfTest()
    {
        var denied = Evaluate("wsl -d Ubuntu-24.04 -- bash -c 'for f in config.js state.js; do echo \"$f\"; node --check $f; done'");
        if (!denied.Decision.Equals("deny", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Fragile inline WSL bash loop was not denied.");
        }

        var safeWsl = Evaluate("wsl -d Ubuntu-24.04 -- bash /mnt/c/Users/Aaron/project/scratch/check_syntax.sh");
        if (!safeWsl.Decision.Equals("allow", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Script-file WSL bash command should be allowed.");
        }

        var safeNative = Evaluate("node --check config.js");
        if (!safeNative.Decision.Equals("allow", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Non-WSL native command should be allowed.");
        }

        var json = """
        {
          "toolCall": {
            "args": {
              "CommandLine": "wsl -d Ubuntu-24.04 -- bash -c 'for f in a; do echo \"$f\"; done'"
            }
          }
        }
        """;
        var extracted = ExtractCommandLine(json);
        if (!extracted.Contains("for f in a", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Antigravity run_command JSON command extraction failed.");
        }
    }

    private static bool IsInlineWslBash(string command)
    {
        var lower = command.ToLowerInvariant();
        return lower.Contains("wsl", StringComparison.Ordinal) &&
            (lower.Contains("bash -c", StringComparison.Ordinal) || lower.Contains("bash -lc", StringComparison.Ordinal));
    }

    private static string? TryGetString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
            {
                return null;
            }
        }

        if (current.ValueKind == JsonValueKind.String)
        {
            return Normalize(current.GetString() ?? "");
        }

        return current.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : Normalize(current.ToString());
    }

    private static string Normalize(string value)
    {
        value = value.Trim();
        return value.Length >= 2 && value[0] == value[^1] && value[0] is '"' or '\''
            ? value[1..^1]
            : value;
    }
}

internal sealed record GuardDecision(
    [property: System.Text.Json.Serialization.JsonPropertyName("decision")] string Decision,
    [property: System.Text.Json.Serialization.JsonPropertyName("reason")] string? Reason = null)
{
    public static GuardDecision Allow() => new("allow");

    public static GuardDecision Deny(string reason) => new("deny", reason);
}
