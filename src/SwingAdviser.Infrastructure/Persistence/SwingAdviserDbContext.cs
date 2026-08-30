using Microsoft.EntityFrameworkCore;

namespace SwingAdviser.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the swing-adviser SQLite database.
/// Business schema (docs/database-schema.md) is added incrementally in later steps;
/// this context is intentionally empty until then.
/// </summary>
public sealed class SwingAdviserDbContext : DbContext
{
    public SwingAdviserDbContext(DbContextOptions<SwingAdviserDbContext> options)
        : base(options)
    {
    }
}
