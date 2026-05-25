namespace Shellfix.Cli;

internal sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
