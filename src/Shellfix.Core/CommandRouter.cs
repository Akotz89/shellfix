using System.Text.RegularExpressions;

namespace Shellfix.Core;

public sealed class CommandRouter
{
    private readonly NativeToolResolver _nativeTools;

    public CommandRouter(NativeToolResolver? nativeTools = null)
    {
        _nativeTools = nativeTools ?? new NativeToolResolver();
    }

    public CommandRoute Classify(string command)
    {
        var trimmed = command.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Unknown("Empty command.", ["empty"]);
        }

        var grammar = CommandGrammar.Analyze(trimmed);
        var routeCommand = grammar.UnwrappedCommand ?? trimmed;

        if (grammar.BlockedReason is not null)
        {
            return new CommandRoute(
                "powershell-file",
                "Explicit WSL command is part of a top-level PowerShell expression, so Shellfix keeps PowerShell ownership.",
                "",
                true,
                "medium",
                DetectRiskFlags(trimmed),
                RoutedCommand: trimmed,
                TopLevelShell: grammar.TopLevelShell,
                OperatorOwner: grammar.OperatorOwner,
                WrapperUnwrapped: false,
                BlockedReason: grammar.BlockedReason);
        }

        if (StartsWithWslCore(routeCommand))
        {
            return new CommandRoute(
                "wsl-direct",
                grammar.Wrapper is null
                    ? "Explicit wsl/wsl.exe command is executed directly with ProcessStartInfo.ArgumentList."
                    : $"{grammar.Wrapper} wrapper around explicit WSL command is unwrapped and executed directly.",
                @"C:\Windows\System32\wsl.exe",
                false,
                "high",
                DetectRiskFlags(trimmed),
                Arguments: CommandTokenizer.ParseCommandArgs(routeCommand),
                RoutedCommand: routeCommand,
                TopLevelShell: grammar.TopLevelShell,
                OperatorOwner: grammar.OperatorOwner,
                WrapperUnwrapped: grammar.Wrapper is not null,
                BlockedReason: grammar.BlockedReason);
        }

        if (ContainsHeredoc(routeCommand))
        {
            return new CommandRoute(
                "unsupported/unknown",
                "Bash heredoc syntax is not safe to run through PowerShell; use explicit wsl/bash routing.",
                "",
                false,
                "low",
                DetectRiskFlags(trimmed),
                RoutedCommand: routeCommand,
                TopLevelShell: grammar.TopLevelShell,
                OperatorOwner: grammar.OperatorOwner,
                WrapperUnwrapped: grammar.Wrapper is not null,
                BlockedReason: "Bare heredoc has no safe owner outside explicit WSL/bash routing.");
        }

        if (TryParseNativeInline(routeCommand, grammar, trimmed, out var inline))
        {
            return inline;
        }

        if (TryParseNativeDirect(routeCommand, grammar, trimmed, out var nativeDirect))
        {
            return nativeDirect;
        }

        return new CommandRoute(
            "powershell-file",
            "Command is not a known WSL/native-inline/native-direct shape, so Shellfix runs it through the PowerShell backend via a temporary script.",
            "",
            true,
            "medium",
            DetectRiskFlags(trimmed),
            RoutedCommand: trimmed,
            TopLevelShell: grammar.TopLevelShell,
            OperatorOwner: grammar.OperatorOwner,
            WrapperUnwrapped: false,
            BlockedReason: grammar.BlockedReason);
    }

    public bool LooksLikeNativeInlineStart(string command)
    {
        var index = 0;
        if (!CommandTokenizer.TryReadCommandToken(command, ref index, out var firstToken))
        {
            return false;
        }

        var normalized = CommandTokenizer.NormalizeCommandName(firstToken);
        if (!IsInlineTool(normalized))
        {
            return false;
        }

        return TryConsumeInlineSwitch(command, ref index, normalized);
    }

    public bool StartsWithWsl(string command) => StartsWithWslCore(command.TrimStart());

    public bool IsBufferedCommandComplete(string command) => CommandTokenizer.IsBufferedCommandComplete(command);

    private bool TryParseNativeInline(string command, CommandGrammarInfo grammar, string originalCommand, out CommandRoute route)
    {
        route = Unknown("Not a native inline command.", []);
        var index = 0;
        if (!CommandTokenizer.TryReadCommandToken(command, ref index, out var firstToken))
        {
            return false;
        }

        var normalized = CommandTokenizer.NormalizeCommandName(firstToken);
        if (!IsInlineTool(normalized))
        {
            return false;
        }

        if (!TryConsumeInlineSwitch(command, ref index, normalized))
        {
            return false;
        }

        CommandTokenizer.SkipWhitespace(command, ref index);
        if (!CommandTokenizer.TryReadInlinePayload(command, ref index, out var payload))
        {
            route = Unknown("Inline interpreter command started but the payload is incomplete.", ["inline-payload-incomplete"]);
            return true;
        }

        var remainder = index < command.Length ? command[index..].Trim() : "";
        var resolved = _nativeTools.Resolve(firstToken) ?? "";
        var args = string.IsNullOrWhiteSpace(remainder) ? [] : CommandTokenizer.ParseCommandArgs(remainder);
        route = new CommandRoute(
            "native-inline-tempfile",
            normalized == "node"
                ? "Inline Node payload is written to a temporary .js file before execution."
                : "Inline Python payload is written to a temporary .py file before execution.",
            resolved,
            false,
            string.IsNullOrWhiteSpace(resolved) ? "medium" : "high",
            DetectRiskFlags(originalCommand),
            firstToken,
            normalized == "node" ? ".js" : ".py",
            payload,
            args,
            RoutedCommand: command,
            TopLevelShell: grammar.TopLevelShell,
            OperatorOwner: grammar.OperatorOwner,
            WrapperUnwrapped: grammar.Wrapper is not null,
            BlockedReason: grammar.BlockedReason);
        return true;
    }

    private bool TryParseNativeDirect(string command, CommandGrammarInfo grammar, string originalCommand, out CommandRoute route)
    {
        route = Unknown("Not a native-direct command.", []);
        var trimmed = StripSimpleNativeRedirection(command);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        var index = 0;
        if (trimmed[index] == '&')
        {
            index++;
            CommandTokenizer.SkipWhitespace(trimmed, ref index);
        }

        if (!CommandTokenizer.TryReadCommandToken(trimmed, ref index, out var firstToken))
        {
            return false;
        }

        var resolved = _nativeTools.Resolve(firstToken);
        if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
        {
            return false;
        }

        if (!_nativeTools.IsNativeFirst(resolved))
        {
            return false;
        }

        var remainder = index < trimmed.Length ? trimmed[index..].Trim() : "";
        if (ContainsPowerShellOnlySyntax(remainder))
        {
            return false;
        }

        route = new CommandRoute(
            "native-direct",
            "Known native executable is executed directly to preserve argument boundaries and the real process exit code.",
            resolved,
            false,
            "high",
            DetectRiskFlags(originalCommand),
            firstToken,
            Arguments: string.IsNullOrWhiteSpace(remainder) ? [] : CommandTokenizer.ParseCommandArgs(remainder),
            RoutedCommand: command,
            TopLevelShell: grammar.TopLevelShell,
            OperatorOwner: grammar.OperatorOwner,
            WrapperUnwrapped: grammar.Wrapper is not null,
            BlockedReason: grammar.BlockedReason);
        return true;
    }

    private bool TryConsumeInlineSwitch(string command, ref int index, string normalizedTool)
    {
        CommandTokenizer.SkipWhitespace(command, ref index);
        if (normalizedTool == "py")
        {
            var beforeVersionSwitch = index;
            if (CommandTokenizer.TryReadCommandToken(command, ref index, out var possibleVersion) &&
                Regex.IsMatch(possibleVersion, @"^-\d+(?:\.\d+)?$"))
            {
                CommandTokenizer.SkipWhitespace(command, ref index);
            }
            else
            {
                index = beforeVersionSwitch;
            }
        }

        var expected = normalizedTool == "node" ? "-e" : "-c";
        return CommandTokenizer.TryReadCommandToken(command, ref index, out var switchToken) &&
            switchToken.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInlineTool(string normalized) => normalized is "python" or "python3" or "py" or "node";

    private static bool StartsWithWslCore(string command) =>
        command.StartsWith("wsl ", StringComparison.OrdinalIgnoreCase) ||
        command.StartsWith("wsl.exe ", StringComparison.OrdinalIgnoreCase);

    private static string StripSimpleNativeRedirection(string command) =>
        Regex.Replace(command.Trim(), @"\s+2>\s*&1\s*$", "", RegexOptions.IgnoreCase).TrimEnd();

    private static bool ContainsPowerShellOnlySyntax(string commandRemainder)
    {
        if (string.IsNullOrWhiteSpace(commandRemainder))
        {
            return false;
        }

        return Regex.IsMatch(commandRemainder, @"(^|[^`]);|&&|\|\||(?<!\|)\|(?!\|)");
    }

    private static bool ContainsHeredoc(string command) => CommandGrammar.ContainsHeredoc(command);

    private static IReadOnlyList<string> DetectRiskFlags(string command)
    {
        var flags = new List<string>();
        if (ContainsHeredoc(command)) { flags.Add("heredoc"); }
        if (command.Contains("&&") || command.Contains("||")) { flags.Add("shell-operator"); }
        if (command.Contains("$PATH") || command.Contains("$HOME")) { flags.Add("bash-variable"); }
        if (Regex.IsMatch(command, @"\b(?:python3?|py)\s+(?:-\d+(?:\.\d+)?\s+)?-c\b", RegexOptions.IgnoreCase)) { flags.Add("inline-python"); }
        if (Regex.IsMatch(command, @"\bnode\s+-e\b", RegexOptions.IgnoreCase)) { flags.Add("inline-node"); }
        if (Regex.IsMatch(command, @"\b(?:curl|Invoke-WebRequest)\b", RegexOptions.IgnoreCase) && command.Contains('{')) { flags.Add("json-payload"); }
        if (Regex.IsMatch(command, @"\s2>\s*&1\s*$")) { flags.Add("stderr-redirect"); }
        return flags;
    }

    private static CommandRoute Unknown(string reason, IReadOnlyList<string> riskFlags) =>
        new("unsupported/unknown", reason, "", false, "low", riskFlags);
}
