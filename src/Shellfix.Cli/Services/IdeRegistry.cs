namespace Shellfix.Cli;

internal static class IdeRegistry
{
    public static IReadOnlyList<IdeDefinition> Build(ShellfixContext context) =>
    [
        new("VS Code", [Path.Combine(context.LocalAppData, "Programs", "Microsoft VS Code", "Code.exe")], ["Visual Studio Code.lnk", "Code.lnk"]),
        new("VS Code Insiders", [Path.Combine(context.LocalAppData, "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe")], ["Visual Studio Code - Insiders.lnk", "Code - Insiders.lnk"]),
        new("Cursor", [Path.Combine(context.LocalAppData, "Programs", "cursor", "Cursor.exe"), Path.Combine(context.LocalAppData, "cursor", "Cursor.exe")], ["Cursor.lnk"]),
        new("Windsurf", [Path.Combine(context.LocalAppData, "Programs", "Windsurf", "Windsurf.exe")], ["Windsurf.lnk"]),
        new("Antigravity IDE", [Path.Combine(context.LocalAppData, "Programs", "Antigravity IDE", "Antigravity IDE.exe")], ["Antigravity IDE.lnk", "Antigravity.lnk"], PatchShortcuts: false)
    ];
}
