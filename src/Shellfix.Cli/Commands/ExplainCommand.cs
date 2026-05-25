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
            Console.Error.WriteLine("[ERROR] Usage: shellfix explain \"<command>\"");
            return 1;
        }

        var command = string.Join(" ", args).Trim();
        var route = Classify(command);

        Console.WriteLine("Shellfix command explanation");
        Console.WriteLine($"Version: {_context.Version}");
        Console.WriteLine($"Backend: {PowerShellBackend.Describe()}");
        Console.WriteLine($"Route: {route.Route}");
        Console.WriteLine($"Reason: {route.Reason}");
        if (!string.IsNullOrWhiteSpace(route.Target))
        {
            Console.WriteLine($"Target: {route.Target}");
        }
        Console.WriteLine($"PowerShell parses payload: {(route.PowerShellParsesPayload ? "yes" : "no")}");
        return 0;
    }

    private static ExplainResult Classify(string command)
    {
        var trimmed = command.TrimStart();
        if (StartsWithWsl(trimmed))
        {
            return new ExplainResult(
                "wsl-direct",
                "Explicit wsl/wsl.exe command is executed by the shim with ProcessStartInfo.ArgumentList.",
                "C:\\Windows\\System32\\wsl.exe",
                false);
        }

        if (TryReadFirstToken(trimmed, out var firstToken, out var remainder))
        {
            var normalized = NormalizeCommandName(firstToken);
            if ((normalized is "python" or "python3" or "py") && StartsWithSwitch(remainder, "-c"))
            {
                return new ExplainResult(
                    "native-inline",
                    "Inline Python payload is written to a temporary .py file before execution.",
                    ResolveWindowsCommand(firstToken),
                    false);
            }

            if (normalized == "node" && StartsWithSwitch(remainder, "-e"))
            {
                return new ExplainResult(
                    "native-inline",
                    "Inline Node payload is written to a temporary .js file before execution.",
                    ResolveWindowsCommand(firstToken),
                    false);
            }

            if (trimmed.StartsWith("& ", StringComparison.OrdinalIgnoreCase) &&
                TryReadFirstToken(trimmed[1..].TrimStart(), out var invokedToken, out _) &&
                IsKnownNative(invokedToken))
            {
                return new ExplainResult(
                    "native-direct",
                    "Known full-path native executable call is executed directly to preserve the real exit code.",
                    invokedToken.Trim('"', '\''),
                    false);
            }
        }

        return new ExplainResult(
            "powershell-file",
            "Command is not a known WSL/native-inline/native-direct shape, so Shellfix runs it through the PowerShell backend via a temporary script.",
            PowerShellBackend.Describe(),
            true);
    }

    private static bool StartsWithWsl(string command) =>
        command.StartsWith("wsl ", StringComparison.OrdinalIgnoreCase) ||
        command.StartsWith("wsl.exe ", StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithSwitch(string remainder, string expectedSwitch)
    {
        return remainder.TrimStart().StartsWith(expectedSwitch + " ", StringComparison.OrdinalIgnoreCase) ||
               remainder.TrimStart().Equals(expectedSwitch, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadFirstToken(string input, out string token, out string remainder)
    {
        token = "";
        remainder = "";
        var trimmed = input.TrimStart();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var sb = new StringBuilder();
        var i = 0;
        var quote = trimmed[i] is '"' or '\'' ? trimmed[i++] : '\0';
        while (i < trimmed.Length)
        {
            var ch = trimmed[i];
            if (quote == '\0' && char.IsWhiteSpace(ch))
            {
                break;
            }

            if (quote != '\0' && ch == quote)
            {
                i++;
                break;
            }

            sb.Append(ch);
            i++;
        }

        token = sb.ToString();
        remainder = i < trimmed.Length ? trimmed[i..] : "";
        return token.Length > 0;
    }

    private static string NormalizeCommandName(string commandName)
    {
        var fileName = Path.GetFileName(commandName.Trim('"', '\''));
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(withoutExtension) ? fileName.ToLowerInvariant() : withoutExtension.ToLowerInvariant();
    }

    private static bool IsKnownNative(string commandName)
    {
        var normalized = NormalizeCommandName(commandName);
        string[] native =
        [
            "python", "python3", "py", "pip", "pip3", "node", "npm", "npx",
            "git", "gh", "dotnet", "docker", "kubectl", "cargo", "rustc", "d2", "dot"
        ];
        return native.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveWindowsCommand(string commandName)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe"),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add(commandName.Trim('"', '\''));
            process.StartInfo.Environment["PATH"] = BuildRefreshedPath();
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return process.ExitCode == 0
                ? stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }

    private static string BuildRefreshedPath()
    {
        var parts = new List<string>();
        foreach (var value in new[]
        {
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User)
        })
        {
            if (string.IsNullOrWhiteSpace(value)) { continue; }
            foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!parts.Contains(part, StringComparer.OrdinalIgnoreCase))
                {
                    parts.Add(part);
                }
            }
        }

        return string.Join(';', parts);
    }

    private sealed record ExplainResult(string Route, string Reason, string Target, bool PowerShellParsesPayload);
}
