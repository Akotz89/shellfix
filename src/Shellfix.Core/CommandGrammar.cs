namespace Shellfix.Core;

public sealed record CommandGrammarInfo(
    string TopLevelShell,
    string OperatorOwner,
    bool HasTopLevelPipe,
    bool HasTopLevelRedirection,
    bool HasTopLevelSequence,
    bool HasTopLevelShellOperator,
    string? Wrapper,
    string? UnwrappedCommand,
    string? BlockedReason)
{
    public bool HasTopLevelPowerShellOperator =>
        HasTopLevelPipe || HasTopLevelRedirection || HasTopLevelSequence;
}

public static class CommandGrammar
{
    public static CommandGrammarInfo Analyze(string command)
    {
        var trimmed = command.Trim();
        var wrapper = TryUnwrapWrapper(trimmed, out var unwrapped);
        var scanTarget = unwrapped ?? trimmed;
        var ops = ScanTopLevelOperators(scanTarget);
        var first = FirstCommandName(scanTarget);
        var topLevelShell = ClassifyTopLevelShell(first, scanTarget, wrapper);
        var operatorOwner = ops.HasPowerShellOperator ? "powershell" :
            ops.HasShellOperator || ContainsHeredoc(scanTarget) ? "bash" : "none";
        var blocked = topLevelShell == "wsl" && ops.HasPowerShellOperator
            ? "Explicit WSL command is wrapped in a top-level PowerShell operator."
            : null;

        return new CommandGrammarInfo(
            topLevelShell,
            operatorOwner,
            ops.HasPipe,
            ops.HasRedirection,
            ops.HasSequence,
            ops.HasShellOperator,
            wrapper,
            unwrapped,
            blocked);
    }

    public static bool ContainsHeredoc(string command) =>
        System.Text.RegularExpressions.Regex.IsMatch(command, @"<<-?\s*['""]?\w+['""]?");

    private static string ClassifyTopLevelShell(string first, string command, string? wrapper)
    {
        if (wrapper is "cmd" or "powershell" or "pwsh")
        {
            var inner = FirstCommandName(command);
            return inner is "wsl" or "wsl.exe" ? "wsl" :
                inner is "cmd" or "cmd.exe" ? "cmd" :
                inner is "powershell" or "powershell.exe" or "pwsh" or "pwsh.exe" ? "powershell" :
                "native";
        }

        return first is "wsl" or "wsl.exe" ? "wsl" :
            first is "cmd" or "cmd.exe" ? "cmd" :
            first is "powershell" or "powershell.exe" or "pwsh" or "pwsh.exe" ? "powershell" :
            "unknown";
    }

    private static string FirstCommandName(string command)
    {
        var index = 0;
        if (!CommandTokenizer.TryReadCommandToken(command, ref index, out var token))
        {
            return "";
        }

        return CommandTokenizer.NormalizeCommandName(token);
    }

    private static string? TryUnwrapWrapper(string command, out string? unwrapped)
    {
        unwrapped = null;
        var index = 0;
        if (!CommandTokenizer.TryReadCommandToken(command, ref index, out var firstToken))
        {
            return null;
        }

        var first = CommandTokenizer.NormalizeCommandName(firstToken);
        if (first is "cmd")
        {
            CommandTokenizer.SkipWhitespace(command, ref index);
            if (CommandTokenizer.TryReadCommandToken(command, ref index, out var switchToken) &&
                (switchToken.Equals("/c", StringComparison.OrdinalIgnoreCase) ||
                 switchToken.Equals("/s", StringComparison.OrdinalIgnoreCase)))
            {
                if (switchToken.Equals("/s", StringComparison.OrdinalIgnoreCase))
                {
                    CommandTokenizer.SkipWhitespace(command, ref index);
                    if (!CommandTokenizer.TryReadCommandToken(command, ref index, out var cSwitch) ||
                        !cSwitch.Equals("/c", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }
                }

                unwrapped = UnescapeWrapperCommand(StripOneOuterQuote(command[index..].Trim()));
                return "cmd";
            }
        }

        if (first is "powershell" or "pwsh")
        {
            while (index < command.Length)
            {
                CommandTokenizer.SkipWhitespace(command, ref index);
                var before = index;
                if (!CommandTokenizer.TryReadCommandToken(command, ref index, out var token))
                {
                    break;
                }

                if (token.Equals("-Command", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("-c", StringComparison.OrdinalIgnoreCase))
                {
                    unwrapped = UnescapeWrapperCommand(StripOneOuterQuote(command[index..].Trim()));
                    return first is "pwsh" ? "pwsh" : "powershell";
                }

                if (!token.StartsWith("-", StringComparison.Ordinal))
                {
                    index = before;
                    break;
                }
            }
        }

        return null;
    }

    private static string StripOneOuterQuote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static string UnescapeWrapperCommand(string value) =>
        value.Replace("\\\"", "\"");

    private static OperatorScan ScanTopLevelOperators(string command)
    {
        var inSingle = false;
        var inDouble = false;
        var escaped = false;
        var hasPipe = false;
        var hasRedirection = false;
        var hasSequence = false;
        var hasShellOperator = false;

        for (var i = 0; i < command.Length; i++)
        {
            var ch = command[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\' && inDouble)
            {
                escaped = true;
                continue;
            }

            if (ch == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                continue;
            }

            if (ch == '"' && !inSingle)
            {
                inDouble = !inDouble;
                continue;
            }

            if (inSingle || inDouble)
            {
                continue;
            }

            if (ch == '|')
            {
                if (i + 1 < command.Length && command[i + 1] == '|')
                {
                    hasShellOperator = true;
                    i++;
                }
                else
                {
                    hasPipe = true;
                }
            }
            else if (ch == '&' && i + 1 < command.Length && command[i + 1] == '&')
            {
                hasShellOperator = true;
                i++;
            }
            else if (ch == '>' || ch == '<')
            {
                if (ch == '<' && i + 1 < command.Length && command[i + 1] == '<')
                {
                    hasShellOperator = true;
                    i++;
                }
                else
                {
                    hasRedirection = true;
                }
            }
            else if (ch == ';')
            {
                hasSequence = true;
            }
        }

        return new OperatorScan(
            hasPipe,
            hasRedirection,
            hasSequence,
            hasShellOperator,
            hasPipe || hasRedirection || hasSequence);
    }

    private sealed record OperatorScan(
        bool HasPipe,
        bool HasRedirection,
        bool HasSequence,
        bool HasShellOperator,
        bool HasPowerShellOperator);
}
