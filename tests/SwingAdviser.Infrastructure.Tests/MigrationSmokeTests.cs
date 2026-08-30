using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Tests;

public class MigrationSmokeTests
{
    [Fact]
    public void Migrate_AppliesAllMigrations_WithoutError()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<SwingAdviserDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;

        using var context = new SwingAdviserDbContext(options);

        context.Database.Migrate();
    }
}
