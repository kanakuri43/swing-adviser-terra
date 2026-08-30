namespace SwingAdviser.Infrastructure.Persistence;

/// <summary>
/// Resolves the runtime SQLite database path per docs/database-schema.md "Runtime database location":
/// fixed to swing-adviser.db next to the running executable, resolved from AppContext.BaseDirectory,
/// with no implicit fallback when the directory is not writable.
/// </summary>
public static class RuntimeDatabasePathResolver
{
    private const string DatabaseFileName = "swing-adviser.db";

    public static string ResolveWritableDatabasePath(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? AppContext.BaseDirectory;
        EnsureWritable(directory);
        return Path.Combine(directory, DatabaseFileName);
    }

    private static void EnsureWritable(string directory)
    {
        var probePath = Path.Combine(directory, $".write-check-{Guid.NewGuid():N}.tmp");

        try
        {
            using (File.Create(probePath))
            {
            }

            File.Delete(probePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"データベース保存先ディレクトリへ書き込めません: {directory}", ex);
        }
    }
}
