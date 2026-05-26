namespace Shellfix.Cli;

internal static class WindowsRegistry
{
    public static void TryEnableLongPaths()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem", writable: true);
            var value = key?.GetValue("LongPathsEnabled");
            if (value is int i && i == 1)
            {
                Log.Ok("Long paths enabled");
                return;
            }
            key?.SetValue("LongPathsEnabled", 1, Microsoft.Win32.RegistryValueKind.DWord);
            Log.Ok("LongPathsEnabled set to 1");
        }
        catch
        {
            Log.Warn("Long paths are not enabled; run as Administrator to enable LongPathsEnabled.");
        }
    }
}
