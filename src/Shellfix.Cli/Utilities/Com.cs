namespace Shellfix.Cli;

internal static class Com
{
    public static dynamic CreateWScriptShell()
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell COM object is unavailable.");
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException("Could not create WScript.Shell COM object.");
    }
}
