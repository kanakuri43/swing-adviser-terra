using SwingAdviser.Application.DailyUpdates;
using SwingAdviser.Domain.Analysis;

namespace SwingAdviser.Infrastructure.Tests;

public class DailyUpdateOrchestratorTests
{
    [Fact]
    public async Task RunsAllElevenStepsInOrderAndCompletesCoreBeforeAiOutcome()
    {
        var store = new FakeRunStore();
        var order = new List<DailyUpdateStep>();
        var progress = new List<DailyUpdateStepProgress>();
        var orchestrator = new DailyUpdateOrchestrator(store, Stages(step =>
        {
            order.Add(step);
            return new DailyUpdateStepResult(step == DailyUpdateStep.RunTechnicalAnalysis ? 5 : 1, 0, step.ToString());
        }), new FixedTimeProvider());

        var result = await orchestrator.RunAsync(Request(), new Progress<DailyUpdateStepProgress>(progress.Add));

        Assert.Equal(Enum.GetValues<DailyUpdateStep>(), order);
        Assert.Equal("Succeeded", result.Status);
        Assert.Equal(11, result.Steps.Count);
        Assert.Equal("Succeeded", store.CompletedStatus);
        Assert.Contains("\"coreAnalysisComplete\":true", store.FinalSummary!);
        Assert.Equal(11, store.Summaries.Count);
    }

    [Fact]
    public async Task AFailedInstrumentStepDoesNotStopLaterStepsAndProducesPartialRun()
    {
        var store = new FakeRunStore();
        var order = new List<DailyUpdateStep>();
        var orchestrator = new DailyUpdateOrchestrator(store, Stages(step =>
        {
            order.Add(step);
            return step == DailyUpdateStep.RefreshMarketData
                ? new DailyUpdateStepResult(9, 1, "One Yahoo request timed out.")
                : new DailyUpdateStepResult(1, 0);
        }), new FixedTimeProvider());

        var result = await orchestrator.RunAsync(Request());

        Assert.Equal(Enum.GetValues<DailyUpdateStep>(), order);
        Assert.Equal("PartiallySucceeded", result.Status);
        Assert.Equal("PartiallySucceeded", store.CompletedStatus);
        Assert.Equal(1, result.Steps[DailyUpdateStep.RefreshMarketData].FailedCount);
    }

    [Fact]
    public async Task AStageExceptionIsAuditedAndLaterStagesStillRun()
    {
        var store = new FakeRunStore();
        var order = new List<DailyUpdateStep>();
        var orchestrator = new DailyUpdateOrchestrator(store, Stages(step =>
        {
            order.Add(step);
            if (step == DailyUpdateStep.VerifyDataAvailability) throw new InvalidOperationException("provider returned malformed data");
            return new DailyUpdateStepResult(1, 0);
        }), new FixedTimeProvider());

        var result = await orchestrator.RunAsync(Request());

        Assert.Equal(Enum.GetValues<DailyUpdateStep>(), order);
        Assert.Equal("PartiallySucceeded", result.Status);
        Assert.Equal(1, result.Steps[DailyUpdateStep.VerifyDataAvailability].FailedCount);
        Assert.Contains("malformed data", store.FinalSummary!);
    }

    [Fact]
    public async Task ReportsTheCurrentStageAndItsWorkItemsBeforeTheStageCompletes()
    {
        var reports = new List<DailyUpdateStepProgress>();
        var stages = Enum.GetValues<DailyUpdateStep>().Select(step => new DelegateStage(step, context =>
        {
            if (step == DailyUpdateStep.RefreshMarketData)
                context.ReportStageProgress("価格を取得中", 12, 100);
            return new DailyUpdateStepResult(1, 0, step.ToString());
        }));

        await new DailyUpdateOrchestrator(new FakeRunStore(), stages, new FixedTimeProvider())
            .RunAsync(Request(), new InlineProgress<DailyUpdateStepProgress>(reports.Add));

        Assert.Contains(reports, report => report.Step == DailyUpdateStep.RefreshMarketData
            && report.Status == "Running"
            && report.CompletedSteps == 0
            && report.CompletedWorkItems == 12
            && report.TotalWorkItems == 100
            && report.Detail == "価格を取得中");
        Assert.Contains(reports, report => report.Step == DailyUpdateStep.VerifyDataAvailability
            && report.Status == "Running"
            && report.CompletedSteps == 1);
    }

    private static DailyUpdateRequest Request() => new(new DateOnly(2026, 8, 31), new DateTime(2026, 8, 31, 7, 0, 0, DateTimeKind.Utc), "test-universe-v1");

    private static IEnumerable<IDailyUpdateStage> Stages(Func<DailyUpdateStep, DailyUpdateStepResult> result) => Enum.GetValues<DailyUpdateStep>().Select(step => new DelegateStage(step, _ => result(step)));

    private sealed class DelegateStage(DailyUpdateStep step, Func<DailyUpdateContext, DailyUpdateStepResult> action) : IDailyUpdateStage
    {
        public DailyUpdateStep Step => step;
        public Task<DailyUpdateStepResult> ExecuteAsync(DailyUpdateContext context, CancellationToken cancellationToken) => Task.FromResult(action(context));
    }

    private sealed class FakeRunStore : IDailyUpdateRunStore
    {
        public List<string> Summaries { get; } = [];
        public string? FinalSummary { get; private set; }
        public string? CompletedStatus { get; private set; }
        public Task<DailyUpdateRun> StartAsync(DateTime startedAtUtc, CancellationToken cancellationToken) => Task.FromResult(new DailyUpdateRun { DailyUpdateRunId = 42, StartedAtUtc = startedAtUtc, Status = "Running" });
        public Task UpdateSummaryAsync(int dailyUpdateRunId, string stepSummaryJson, CancellationToken cancellationToken) { Summaries.Add(stepSummaryJson); return Task.CompletedTask; }
        public Task CompleteAsync(int dailyUpdateRunId, string status, DateTime completedAtUtc, string stepSummaryJson, CancellationToken cancellationToken) { CompletedStatus = status; FinalSummary = stepSummaryJson; return Task.CompletedTask; }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 31, 7, 30, 0, TimeSpan.Zero);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
