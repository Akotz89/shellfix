namespace Shellfix.Cli;

internal static class Temp
{
    public static string Create(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + " " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
