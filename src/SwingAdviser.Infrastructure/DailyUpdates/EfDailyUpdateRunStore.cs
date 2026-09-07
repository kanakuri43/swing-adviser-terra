using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.DailyUpdates;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.DailyUpdates;

public sealed class EfDailyUpdateRunStore(SwingAdviserDbContext context) : IDailyUpdateRunStore
{
    public async Task<DailyUpdateRun> StartAsync(DateTime startedAtUtc, CancellationToken cancellationToken)
    {
        var interruptedRuns = await context.DailyUpdateRuns.Where(existing => existing.Status == "Running").ToListAsync(cancellationToken);
        foreach (var interrupted in interruptedRuns)
        {
            interrupted.Status = "Failed";
            interrupted.CompletedAtUtc = startedAtUtc;
            interrupted.StepSummaryJson = "{\"schemaVersion\":\"daily-update-summary-v1\",\"errorKind\":\"Interrupted\",\"coreAnalysisComplete\":false}";
        }
        var interruptedCheckpoints = await context.DailyUpdateFetchCheckpoints.Where(checkpoint => checkpoint.Status == "Running").ToListAsync(cancellationToken);
        foreach (var checkpoint in interruptedCheckpoints)
        {
            checkpoint.Status = "Interrupted";
            checkpoint.CompletedAtUtc = startedAtUtc;
        }
        if (interruptedRuns.Count != 0 || interruptedCheckpoints.Count != 0) await context.SaveChangesAsync(cancellationToken);
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
        var activeCheckpoints = await context.DailyUpdateFetchCheckpoints.Where(checkpoint => checkpoint.DailyUpdateRunId == dailyUpdateRunId && checkpoint.Status == "Running").ToListAsync(cancellationToken);
        foreach (var checkpoint in activeCheckpoints)
        {
            checkpoint.Status = "Interrupted";
            checkpoint.CompletedAtUtc = completedAtUtc;
        }
        await context.SaveChangesAsync(cancellationToken);
    }
}
