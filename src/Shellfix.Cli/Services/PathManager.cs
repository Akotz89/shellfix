namespace Shellfix.Cli;

internal sealed class PathManager
{
    public string GetUserPath() => Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";

    public bool EnsurePrepended(string path)
    {
        var current = GetUserPath();
        if (ContainsEntry(current, path))
        {
            Log.Ok($"{path} already in user PATH");
            return false;
        }

        Environment.SetEnvironmentVariable("Path", string.IsNullOrWhiteSpace(current) ? path : $"{path};{current}", EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("Path", $"{path};{Environment.GetEnvironmentVariable("Path")}");
        Log.Ok($"Added to user PATH: {path}");
        return true;
    }

    public void RemoveEntries(params string[] paths)
    {
        var current = GetUserPath();
        var remove = paths.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = string.Join(";", Split(current).Where(p => !remove.Contains(Normalize(p))));
        Environment.SetEnvironmentVariable("Path", next, EnvironmentVariableTarget.User);
        Log.Ok("Removed Shellfix entries from user PATH");
    }

    public CheckResult Check(string shimPath, string installRoot, string binDir)
    {
        var userPath = GetUserPath();
        var hasInstallRoot = ContainsEntry(userPath, installRoot);
        var hasBin = ContainsEntry(userPath, binDir);
        var beforeSystem = PathPrecedesSystem(userPath, binDir);
        var pass = hasInstallRoot && hasBin && beforeSystem;
        return new CheckResult
        {
            Name = "path",
            Status = pass ? "pass" : "fail",
            Message = pass ? $"PATH routes shellfix before system PowerShell: {shimPath}" : "Shellfix PATH ordering is incomplete.",
            Remediation = "Run shellfix install, then restart the IDE from a patched shortcut."
        };
    }

    public static void SelfTest()
    {
        const string current = @"C:\Windows;C:\Tools";
        if (ContainsEntry(current, @"C:\Windows\")) { return; }
        throw new InvalidOperationException("PATH comparison failed.");
    }

    private static bool PathPrecedesSystem(string path, string binDir)
    {
        var entries = Split(path).Select(Normalize).ToList();
        var binIndex = entries.FindIndex(p => p.Equals(Normalize(binDir), StringComparison.OrdinalIgnoreCase));
        var psIndex = entries.FindIndex(p => p.EndsWith(@"windows\system32\windowspowershell\v1.0", StringComparison.OrdinalIgnoreCase));
        return binIndex >= 0 && (psIndex < 0 || binIndex < psIndex);
    }

    private static bool ContainsEntry(string path, string entry) =>
        Split(path).Any(p => Normalize(p).Equals(Normalize(entry), StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Split(string path) =>
        path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Normalize(string path) => path.Trim().TrimEnd('\\', '/');
}
