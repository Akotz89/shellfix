
namespace Shellfix.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            return new ShellfixCli(new ShellfixContext()).Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] {ex.Message}");
            return 1;
        }
    }
}
