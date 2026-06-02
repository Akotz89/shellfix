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

        var preferred = PreferredCandidate(trimmed);
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        var extensions = new List<string>();
        if (Path.HasExtension(trimmed))
        {
            extensions.Add("");
        }
        else
        {
            var pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD;.PS1";
            if (string.IsNullOrWhiteSpace(pathext))
            {
                pathext = ".COM;.EXE;.BAT;.CMD;.PS1";
            }
            extensions.AddRange(pathext.Split(';', StringSplitOptions.RemoveEmptyEntries));
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

                if (File.Exists(candidate) && !ShouldSkipPathCandidate(trimmed, candidate))
                {
                    return candidate;
                }
            }
        }

        foreach (var candidate in FallbackCandidates(trimmed))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> FallbackCandidates(string commandName)
    {
        var normalized = CommandTokenizer.NormalizeCommandName(commandName);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return normalized switch
        {
            "git" => ExistingRoots(programFiles, programFilesX86)
                .Select(root => Path.Combine(root, "Git", "cmd", "git.exe")),
            "gh" => ExistingRoots(programFiles, programFilesX86)
                .Select(root => Path.Combine(root, "GitHub CLI", "gh.exe")),
            "dotnet" => ExistingRoots(programFiles)
                .Select(root => Path.Combine(root, "dotnet", "dotnet.exe")),
            "wsl" => ExistingRoots(systemRoot)
                .Select(root => Path.Combine(root, "System32", "wsl.exe")),
            "node" => ExistingRoots(userProfile, @"C:\nvm4w")
                .Select(root => Path.Combine(root, root.EndsWith("nvm4w", StringComparison.OrdinalIgnoreCase) ? "nodejs" : Path.Combine("AppData", "Local", "nvm"), "node.exe")),
            "npm" => ExistingRoots(userProfile, @"C:\nvm4w")
                .Select(root => Path.Combine(root, root.EndsWith("nvm4w", StringComparison.OrdinalIgnoreCase) ? "nodejs" : Path.Combine("AppData", "Roaming", "npm"), "npm.cmd")),
            "npx" => ExistingRoots(userProfile, @"C:\nvm4w")
                .Select(root => Path.Combine(root, root.EndsWith("nvm4w", StringComparison.OrdinalIgnoreCase) ? "nodejs" : Path.Combine("AppData", "Roaming", "npm"), "npx.cmd")),
            "python" or "python3" => ExistingRoots(localAppData)
                .SelectMany(root => Directory.Exists(Path.Combine(root, "Programs", "Python"))
                    ? Directory.EnumerateDirectories(Path.Combine(root, "Programs", "Python"), "Python*")
                    : [])
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => Path.Combine(path, "python.exe")),
            "py" => ExistingRoots(systemRoot)
                .Select(root => Path.Combine(root, "py.exe")),
            _ => []
        };
    }

    private static IEnumerable<string> ExistingRoots(params string[] roots) =>
        roots.Where(root => !string.IsNullOrWhiteSpace(root));

    private static string? PreferredCandidate(string commandName)
    {
        var normalized = CommandTokenizer.NormalizeCommandName(commandName);
        var candidate = normalized switch
        {
            "npm" => @"C:\nvm4w\nodejs\npm.cmd",
            "npx" => @"C:\nvm4w\nodejs\npx.cmd",
            _ => ""
        };
        return File.Exists(candidate) ? candidate : null;
    }

    private static bool ShouldSkipPathCandidate(string commandName, string candidate)
    {
        var normalized = CommandTokenizer.NormalizeCommandName(commandName);
        if (normalized is not ("npm" or "npx"))
        {
            return false;
        }

        var dir = Path.GetDirectoryName(candidate);
        return !string.IsNullOrWhiteSpace(dir) &&
            !File.Exists(Path.Combine(dir, "node.exe"));
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
