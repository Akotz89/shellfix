namespace Shellfix.Cli;

internal static class PowerShellPolicy
{
    public static void EnsureRemoteSigned()
    {
        var get = ProcessRunner.Run(PowerShellBackend.WindowsPowerShellPath, ["-NoProfile", "-Command", "Get-ExecutionPolicy -Scope CurrentUser"]);
        var policy = get.Stdout.Trim();
        if (string.IsNullOrWhiteSpace(policy))
        {
            Log.Ok("ExecutionPolicy check completed");
            return;
        }
        if (policy.Equals("Restricted", StringComparison.OrdinalIgnoreCase) || policy.Equals("Undefined", StringComparison.OrdinalIgnoreCase))
        {
            var set = ProcessRunner.Run(PowerShellBackend.WindowsPowerShellPath, ["-NoProfile", "-Command", "Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser -Force"]);
            if (set.ExitCode == 0) { Log.Ok("ExecutionPolicy set to RemoteSigned"); }
            else { Log.Warn("Could not set ExecutionPolicy; agent temp scripts may be blocked."); }
        }
        else
        {
            Log.Ok($"ExecutionPolicy: {policy}");
        }
    }
}
