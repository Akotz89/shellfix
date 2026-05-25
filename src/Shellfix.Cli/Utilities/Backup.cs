namespace Shellfix.Cli;

internal static class Backup
{
    public static string Copy(ShellfixContext context, string source, string category)
    {
        var dir = Path.Combine(context.BackupRoot, category);
        Directory.CreateDirectory(dir);
        var backup = Path.Combine(dir, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Hashing.Short(source)}-{Path.GetFileName(source)}");
        File.Copy(source, backup, overwrite: true);
        return backup;
    }

    public static void PreferOldestRecordedBackups(ShellfixContext context, InstallState state)
    {
        if (state.Profile is not null)
        {
            var oldest = FindOldest(context, "profile", Path.GetFileName(state.Profile.ProfilePath));
            if (oldest is not null) { state.Profile.BackupPath = oldest; }
        }

        if (state.AntigravitySettings is not null)
        {
            var oldest = FindOldest(context, "settings", Path.GetFileName(state.AntigravitySettings.SettingsPath));
            if (oldest is not null) { state.AntigravitySettings.BackupPath = oldest; }
        }
    }

    private static string? FindOldest(ShellfixContext context, string category, string fileName)
    {
        var dir = Path.Combine(context.BackupRoot, category);
        if (!Directory.Exists(dir)) { return null; }
        return Directory.EnumerateFiles(dir, "*-" + fileName)
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.CreationTimeUtc)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }
}
