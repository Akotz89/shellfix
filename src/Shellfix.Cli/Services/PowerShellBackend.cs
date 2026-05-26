namespace Shellfix.Cli;

internal static class PowerShellBackend
{
    public const string Pwsh7Path = @"C:\Program Files\PowerShell\7\pwsh.exe";
    public const string WindowsPowerShellPath = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";

    public static string Describe()
    {
        var path = File.Exists(Pwsh7Path) ? Pwsh7Path : WindowsPowerShellPath;
        var result = ProcessRunner.Run(path, ["-NoProfile", "-Command", "$PSVersionTable.PSVersion.ToString()"]);
        return result.ExitCode == 0 ? $"{path} ({result.Stdout.Trim()})" : path;
    }
}
