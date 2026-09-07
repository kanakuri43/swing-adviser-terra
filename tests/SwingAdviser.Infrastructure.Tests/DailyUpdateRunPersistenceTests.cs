using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Infrastructure.DailyUpdates;
using SwingAdviser.Infrastructure.Persistence;
using SwingAdviser.Domain.Analysis;

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

    [Fact]
    public async Task OverviewReader_UsesLatestDurableRunAndExposesStepCounts()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var context = CreateContext(connection);
        await context.Database.MigrateAsync();
        var started = new DateTime(2026, 8, 31, 7, 0, 0, DateTimeKind.Utc);
        context.DailyUpdateRuns.AddRange(
            new SwingAdviser.Domain.Analysis.DailyUpdateRun { StartedAtUtc = started.AddDays(-1), CompletedAtUtc = started.AddDays(-1).AddMinutes(1), Status = "Succeeded" },
            new SwingAdviser.Domain.Analysis.DailyUpdateRun
            {
                StartedAtUtc = started,
                Status = "Running",
                StepSummaryJson = """{"steps":[{"step":"RefreshMarketData","succeeded":10,"failed":2,"detail":"Market data refreshed."},{"step":"VerifyDataAvailability","succeeded":8,"failed":2,"detail":"Final bars checked."}]}""",
            });
        await context.SaveChangesAsync();

        var overview = await new EfDailyUpdateOverviewReader(context).GetLatestAsync();

        Assert.Equal("実行中", overview.Status);
        Assert.Equal(2, overview.CompletedSteps);
        Assert.Equal(18, overview.SucceededCount);
        Assert.Equal(4, overview.FailedCount);
        Assert.Equal("Final bars checked.", overview.Detail);
    }

    [Fact]
    public async Task OverviewReader_HandlesAnInterruptedRunWithAnEmptyStepArray()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var context = CreateContext(connection);
        await context.Database.MigrateAsync();
        context.DailyUpdateRuns.Add(new SwingAdviser.Domain.Analysis.DailyUpdateRun
        {
            StartedAtUtc = new DateTime(2026, 8, 31, 11, 38, 0, DateTimeKind.Utc),
            CompletedAtUtc = new DateTime(2026, 8, 31, 11, 41, 0, DateTimeKind.Utc),
            Status = "Failed",
            StepSummaryJson = """{"schemaVersion":"daily-update-summary-v1","coreAnalysisComplete":false,"steps":[]}""",
        });
        await context.SaveChangesAsync();

        var overview = await new EfDailyUpdateOverviewReader(context).GetLatestAsync();

        Assert.Equal("失敗", overview.Status);
        Assert.Equal(0, overview.CompletedSteps);
        Assert.Equal(0, overview.SucceededCount);
        Assert.Equal(0, overview.FailedCount);
        Assert.Equal("保存済みの日次更新結果です。", overview.Detail);
    }

    [Fact]
    public async Task RunStore_ClosesAbandonedRunAndCheckpointBeforeStartingANewRun()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var context = CreateContext(connection);
        await context.Database.MigrateAsync();
        var abandonedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var abandoned = new DailyUpdateRun { StartedAtUtc = abandonedAt, Status = "Running" };
        context.DailyUpdateRuns.Add(abandoned);
        await context.SaveChangesAsync();
        context.DailyUpdateFetchCheckpoints.Add(new DailyUpdateFetchCheckpoint
        {
            DailyUpdateRunId = abandoned.DailyUpdateRunId, EvaluationBarDate = new DateOnly(2026, 8, 31), SourceKind = "YahooFinanceChartApiV8",
            TargetKey = "1", Status = "Running", StartedAtUtc = abandonedAt, ValidUntilUtc = abandonedAt.AddHours(6),
        });
        await context.SaveChangesAsync();

        var resumedAt = abandonedAt.AddHours(1);
        await new EfDailyUpdateRunStore(context).StartAsync(resumedAt, CancellationToken.None);
        context.ChangeTracker.Clear();

        var restored = await context.DailyUpdateRuns.OrderBy(run => run.DailyUpdateRunId).FirstAsync();
        var checkpoint = await context.DailyUpdateFetchCheckpoints.SingleAsync();
        Assert.Equal("Failed", restored.Status);
        Assert.Equal(resumedAt, restored.CompletedAtUtc);
        Assert.Contains("Interrupted", restored.StepSummaryJson!);
        Assert.Equal("Interrupted", checkpoint.Status);
        Assert.Equal(resumedAt, checkpoint.CompletedAtUtc);
    }

    private static SwingAdviserDbContext CreateContext(SqliteConnection connection) => new(new DbContextOptionsBuilder<SwingAdviserDbContext>().UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
}
