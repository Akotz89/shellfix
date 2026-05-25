namespace Shellfix.Cli;

internal static class Log
{
    public static void Step(string message) => Console.WriteLine($"\n[INFO] {message}");
    public static void Ok(string message) => Console.WriteLine($"  [OK] {message}");
    public static void Warn(string message) => Console.WriteLine($"  [WARN] {message}");
    public static void Error(string message) => Console.Error.WriteLine($"  [ERROR] {message}");
    public static void Success(string message) => Console.WriteLine($"\n{message}");
}
