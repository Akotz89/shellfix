namespace Shellfix.Cli;

internal sealed class TestCommand
{
    private readonly ShellfixContext _context;

    public TestCommand(ShellfixContext context)
    {
        _context = context;
    }

    public int Run(CommandOptions options)
    {
        var failures = 0;

        if (options.Has("antigravity-settings"))
        {
            return RunTest("Antigravity settings merge", () => new AntigravitySettingsManager(_context).SelfTest());
        }

        if (options.Has("shortcuts"))
        {
            return RunTest("Shortcut patch/restore", () => new ShortcutManager(_context).SelfTest());
        }

        if (!options.Has("shortcuts"))
        {
            failures += RunTest("Antigravity settings merge", () => new AntigravitySettingsManager(_context).SelfTest());
            failures += RunTest("Profile install/remove", () => ProfileInstaller.SelfTest(_context));
            failures += RunTest("PATH insertion", PathManager.SelfTest);
            failures += RunTest("State serialization", () => StateStore.SelfTest(_context));
        }
        failures += RunTest("Shortcut patch/restore", () => new ShortcutManager(_context).SelfTest());

        return failures == 0 ? 0 : 1;
    }

    private static int RunTest(string name, Action action)
    {
        try
        {
            action();
            Log.Ok($"{name} passed");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error($"{name} failed: {ex.Message}");
            return 1;
        }
    }
}
