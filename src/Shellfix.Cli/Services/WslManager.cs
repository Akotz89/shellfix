namespace Shellfix.Cli;

internal static class WslManager
{
    public static void Validate(string distro)
    {
        var result = ProcessRunner.Run("wsl.exe", ["-d", distro, "-e", "echo", "ok"]);
        if (result.ExitCode != 0 || !result.Stdout.Contains("ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"WSL distribution '{distro}' is not available. Run 'wsl --list --quiet' to see installed distributions.");
        }
        Log.Ok($"WSL distribution available: {distro}");
    }

    public static CheckResult Check(string distro)
    {
        var result = ProcessRunner.Run("wsl.exe", ["-d", distro, "-e", "echo", "ok"]);
        var pass = result.ExitCode == 0 && result.Stdout.Contains("ok", StringComparison.OrdinalIgnoreCase);
        return new CheckResult
        {
            Name = "wsl",
            Status = pass ? "pass" : "fail",
            Message = pass ? $"WSL distribution available: {distro}" : $"WSL distribution unavailable: {distro}",
            Remediation = "Install WSL or rerun shellfix install --wsl-distro <name>."
        };
    }
}
