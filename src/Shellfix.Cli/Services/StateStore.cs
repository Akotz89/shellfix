namespace Shellfix.Cli;

internal sealed class StateStore
{
    private readonly ShellfixContext _context;

    public StateStore(ShellfixContext context)
    {
        _context = context;
    }

    public InstallState? Load()
    {
        if (!File.Exists(_context.StatePath)) { return null; }
        return JsonSerializer.Deserialize<InstallState>(File.ReadAllText(_context.StatePath, Utf8.NoBom), Json.Options);
    }

    public void Save(InstallState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_context.StatePath)!);
        File.WriteAllText(_context.StatePath, JsonSerializer.Serialize(state, Json.Options), Utf8.NoBom);
        Log.Ok($"State saved: {_context.StatePath}");
    }

    public void Delete()
    {
        if (File.Exists(_context.StatePath)) { File.Delete(_context.StatePath); }
    }

    public static void SelfTest(ShellfixContext context)
    {
        var root = Temp.Create("shellfix state test");
        try
        {
            var fake = new ShellfixContextForTests(context, root);
            var store = new StateStore(fake);
            var state = InstallState.Create(fake, Path.Combine(root, "bin"), "Ubuntu-24.04");
            state.Shortcuts.Add(new ShortcutPatchState { IdeName = "Test", ShortcutPath = "a", BackupPath = "b", LauncherPath = "c" });
            store.Save(state);
            var loaded = store.Load();
            if (loaded is null || loaded.Shortcuts.Count != 1 || loaded.WslDistro != "Ubuntu-24.04")
            {
                throw new InvalidOperationException("State round-trip failed.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
