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

        if (options.Has("incidents"))
        {
            return RunTest("Incident route fixtures", () => RunIncidentRouteFixtures(_context, options));
        }

        if (options.Has("antigravity-guard"))
        {
            return RunTest("Antigravity run_command guard", AntigravityRunCommandGuard.SelfTest);
        }

        if (!options.Has("shortcuts"))
        {
            failures += RunTest("Antigravity settings merge", () => new AntigravitySettingsManager(_context).SelfTest());
            failures += RunTest("Antigravity run_command guard", AntigravityRunCommandGuard.SelfTest);
            failures += RunTest("Profile install/remove", () => ProfileInstaller.SelfTest(_context));
            failures += RunTest("PATH insertion", PathManager.SelfTest);
            failures += RunTest("State serialization", () => StateStore.SelfTest(_context));
            failures += RunTest("Incident route fixtures", () => RunIncidentRouteFixtures(_context, options));
        }
        failures += RunTest("Shortcut patch/restore", () => new ShortcutManager(_context).SelfTest());

        return failures == 0 ? 0 : 1;
    }

    private static void RunIncidentRouteFixtures(ShellfixContext context, CommandOptions options)
    {
        var fixturePath = options.Get("fixture", Path.Combine(context.RepoRoot, "tests", "incident-routes.json"));
        if (!File.Exists(fixturePath))
        {
            throw new FileNotFoundException("Incident route fixture file not found.", fixturePath);
        }

        var fixtures = JsonSerializer.Deserialize<List<IncidentRouteFixture>>(File.ReadAllText(fixturePath, Utf8.NoBom), Json.Options) ?? [];
        var router = new CommandRouter();
        var nativeTools = new NativeToolResolver();
        var failures = new List<string>();

        foreach (var fixture in fixtures)
        {
            var command = fixture.Command;
            if (!string.IsNullOrWhiteSpace(fixture.RequiredTool))
            {
                var resolved = nativeTools.Resolve(fixture.RequiredTool);
                if (string.IsNullOrWhiteSpace(resolved))
                {
                    continue;
                }

                command = command.Replace("{{" + fixture.RequiredTool + "}}", resolved);
            }

            var route = router.Classify(command);
            if (!route.Route.Equals(fixture.ExpectedRoute, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{fixture.Name}: expected {fixture.ExpectedRoute}, got {route.Route} ({route.Reason})");
            }

            if (route.PowerShellParsesPayload && fixture.ExpectedRoute != "powershell-file")
            {
                failures.Add($"{fixture.Name}: route {route.Route} still allows PowerShell to parse payload");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", failures));
        }
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

    private sealed record IncidentRouteFixture(string Name, string Command, string ExpectedRoute, string Source, string? RequiredTool = null);
}
