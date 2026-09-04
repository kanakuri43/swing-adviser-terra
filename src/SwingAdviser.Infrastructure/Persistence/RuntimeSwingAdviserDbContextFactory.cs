using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SwingAdviser.Infrastructure.Persistence;

/// <summary>Creates the one runtime context at the fixed executable-adjacent database location.</summary>
public static class RuntimeSwingAdviserDbContextFactory
{
    public static SwingAdviserDbContext CreateMigratedContext()
    {
        var context = CreateContext();
        context.Database.Migrate();
        return context;
    }

    public static SwingAdviserDbContext CreateContext(string? baseDirectory = null)
    {
        var databasePath = RuntimeDatabasePathResolver.ResolveWritableDatabasePath(baseDirectory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<SwingAdviserDbContext>()
            .UseSqlite(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        var context = new SwingAdviserDbContext(options);
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode='WAL';");
        return context;
    }
}
