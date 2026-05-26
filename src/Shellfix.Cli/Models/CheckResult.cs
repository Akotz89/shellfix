namespace Shellfix.Cli;

internal sealed class CheckResult
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public string Remediation { get; set; } = "";
}
