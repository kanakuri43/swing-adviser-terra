using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.DailyUpdates;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.DailyUpdates;

public sealed class EfDailyUpdateRunStore(SwingAdviserDbContext context) : IDailyUpdateRunStore
{
    public async Task<DailyUpdateRun> StartAsync(DateTime startedAtUtc, CancellationToken cancellationToken)
    {
        var run = new DailyUpdateRun { StartedAtUtc = startedAtUtc, Status = "Running" };
        context.DailyUpdateRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task UpdateSummaryAsync(int dailyUpdateRunId, string stepSummaryJson, CancellationToken cancellationToken)
    {
        var run = await context.DailyUpdateRuns.SingleAsync(run => run.DailyUpdateRunId == dailyUpdateRunId, cancellationToken);
        run.StepSummaryJson = stepSummaryJson;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(int dailyUpdateRunId, string status, DateTime completedAtUtc, string stepSummaryJson, CancellationToken cancellationToken)
    {
        var run = await context.DailyUpdateRuns.SingleAsync(run => run.DailyUpdateRunId == dailyUpdateRunId, cancellationToken);
        run.Status = status;
        run.CompletedAtUtc = completedAtUtc;
        run.StepSummaryJson = stepSummaryJson;
        await context.SaveChangesAsync(cancellationToken);
    }
}
