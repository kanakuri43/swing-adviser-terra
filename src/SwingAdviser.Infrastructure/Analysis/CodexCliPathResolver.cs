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
}
