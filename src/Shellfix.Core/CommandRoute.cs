namespace Shellfix.Core;

public sealed record CommandRoute(
    string Route,
    string Reason,
    string Target,
    bool PowerShellParsesPayload,
    string Confidence,
    IReadOnlyList<string> RiskFlags,
    string? Tool = null,
    string? ScriptExtension = null,
    string? InlinePayload = null,
    IReadOnlyList<string>? Arguments = null)
{
    public bool Is(string route) => Route.Equals(route, StringComparison.OrdinalIgnoreCase);
}
