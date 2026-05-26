namespace Shellfix.Cli;

internal sealed class DoctorReport
{
    public string Version { get; set; } = "";
    public string InstallRoot { get; set; } = "";
    public string StatePath { get; set; } = "";
    public string ShimPath { get; set; } = "";
    public string ShimHash { get; set; } = "";
    public string PowerShellBackend { get; set; } = "";
    public string WslDistro { get; set; } = "";
    public List<CheckResult> Checks { get; set; } = [];
}
