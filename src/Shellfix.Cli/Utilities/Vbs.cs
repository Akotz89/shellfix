namespace Shellfix.Cli;

internal static class Vbs
{
    public static string StringLiteral(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
