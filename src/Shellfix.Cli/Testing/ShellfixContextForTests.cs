namespace Shellfix.Cli;

internal sealed class ShellfixContextForTests : ShellfixContext
{
    public ShellfixContextForTests(ShellfixContext source, string root)
    {
        UserProfile = root;
        LocalAppData = Path.Combine(root, "AppData", "Local");
        AppData = Path.Combine(root, "AppData", "Roaming");
        RepoRoot = source.RepoRoot;
        ExecutablePath = source.ExecutablePath;
        Version = source.Version;
    }
}
