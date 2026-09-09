namespace SwingAdviser.Infrastructure.Analysis;

/// <summary>Uses the same Codex CLI discovery order as the existing Stock Simulator desktop application.</summary>
public static class CodexCliPathResolver
{
    public static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("SWING_ADVISER_CODEX_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var npmExecutable = Path.Combine(appData, "npm", "node_modules", "@openai", "codex", "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");
        if (File.Exists(npmExecutable)) return npmExecutable;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var directory in new[]
                 {
                     Path.Combine(localAppData, "OpenAI", "Codex", "bin"),
                     Path.Combine(localAppData, "Programs", "OpenAI", "Codex", "bin"),
                 })
        {
            var desktopExecutable = FindExecutableInDirectory(directory);
            if (desktopExecutable is not null) return desktopExecutable;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "codex.exe");
                if (candidate.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException) { }
        }
        return "codex";
    }

    internal static string? FindExecutableInDirectory(string directory)
    {
        try
        {
            var direct = Path.Combine(directory, "codex.exe");
            if (File.Exists(direct)) return direct;

            return Directory.Exists(directory)
                ? Directory.EnumerateDirectories(directory)
                    .Select(child => Path.Combine(child, "codex.exe"))
                    .Where(File.Exists)
                    .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                    .ThenBy(path => path, StringComparer.Ordinal)
                    .FirstOrDefault()
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
