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

    public static SwingAdviserDbContext CreateContext()
    {
        var databasePath = RuntimeDatabasePathResolver.ResolveWritableDatabasePath();
        var options = new DbContextOptionsBuilder<SwingAdviserDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new SwingAdviserDbContext(options);
    }
}
