namespace Shellfix.Core;

public sealed class NativeToolResolver
{
    private static readonly string[] NativeFirst =
    [
        "python", "python3", "py", "pip", "pip3", "node", "npm", "npx",
        "git", "gh", "dotnet", "docker", "kubectl", "cargo", "rustc",
        "d2", "dot", "where"
    ];

    public bool IsNativeFirst(string commandName)
    {
        var normalized = CommandTokenizer.NormalizeCommandName(commandName);
        return NativeFirst.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    public string? Resolve(string commandName)
    {
        var trimmed = commandName.Trim('"', '\'');
        if ((Path.IsPathRooted(trimmed) || trimmed.Contains('\\') || trimmed.Contains('/')) && File.Exists(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        var extensions = new List<string>();
        if (Path.HasExtension(trimmed))
        {
            extensions.Add("");
        }
        else
        {
            var pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD;.PS1";
            extensions.AddRange(pathext.Split(';', StringSplitOptions.RemoveEmptyEntries));
            extensions.Add("");
        }

        var path = BuildRefreshedPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(dir.Trim(), trimmed + ext);
                }
                catch
                {
                    continue;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public static string BuildRefreshedPath()
    {
        var parts = new List<string>();
        foreach (var value in new[]
        {
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User)
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var part in value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                if (!parts.Contains(part.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    parts.Add(part.Trim());
                }
            }
        }

        return string.Join(Path.PathSeparator, parts);
    }
}
