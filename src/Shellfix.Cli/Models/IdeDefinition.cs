namespace Shellfix.Cli;

internal sealed record IdeDefinition(string Name, string[] ExePaths, string[] ShortcutNames, bool PatchShortcuts = true);
