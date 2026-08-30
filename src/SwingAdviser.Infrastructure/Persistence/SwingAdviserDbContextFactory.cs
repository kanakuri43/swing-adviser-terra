using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SwingAdviser.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by `dotnet ef migrations` tooling. Not used at application runtime;
/// the running app builds its own DbContextOptions using RuntimeDatabasePathResolver.
/// </summary>
public sealed class SwingAdviserDbContextFactory : IDesignTimeDbContextFactory<SwingAdviserDbContext>
{
    public SwingAdviserDbContext CreateDbContext(string[] args)
    {
        var databasePath = RuntimeDatabasePathResolver.ResolveWritableDatabasePath();

        var optionsBuilder = new DbContextOptionsBuilder<SwingAdviserDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .UseSnakeCaseNamingConvention();

        return new SwingAdviserDbContext(optionsBuilder.Options);
    }
}
