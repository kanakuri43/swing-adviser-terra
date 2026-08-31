using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Infrastructure.DailyUpdates;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Tests;

public class DailyUpdateRunPersistenceTests
{
    [Fact]
    public async Task RunStore_PersistsRunningProgressAndTerminalSummary()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var context = CreateContext(connection);
        await context.Database.MigrateAsync();
        var store = new EfDailyUpdateRunStore(context);
        var started = new DateTime(2026, 8, 31, 7, 0, 0, DateTimeKind.Utc);

        var run = await store.StartAsync(started, CancellationToken.None);
        await store.UpdateSummaryAsync(run.DailyUpdateRunId, "{\"steps\":[\"RefreshMarketData\"]}", CancellationToken.None);
        await store.CompleteAsync(run.DailyUpdateRunId, "PartiallySucceeded", started.AddMinutes(5), "{\"coreAnalysisComplete\":true}", CancellationToken.None);
        context.ChangeTracker.Clear();

        var restored = await context.DailyUpdateRuns.SingleAsync();
        Assert.Equal("PartiallySucceeded", restored.Status);
        Assert.Equal(started.AddMinutes(5), restored.CompletedAtUtc);
        Assert.Contains("coreAnalysisComplete", restored.StepSummaryJson!);
    }

    private static SwingAdviserDbContext CreateContext(SqliteConnection connection) => new(new DbContextOptionsBuilder<SwingAdviserDbContext>().UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
}
