using System.Text.Json;

namespace SwingAdviser.Application.DailyUpdates;

/// <summary>
/// Coordinates the daily workflow without placing orders or creating trade executions. Core analysis is complete at step 9;
/// publishing and AI-queue insertion are reported separately and never wait for AI analysis to finish.
/// </summary>
public sealed class DailyUpdateOrchestrator
{
    private static readonly DailyUpdateStep[] OrderedSteps = Enum.GetValues<DailyUpdateStep>();
    private readonly IDailyUpdateRunStore _store;
    private readonly IReadOnlyDictionary<DailyUpdateStep, IDailyUpdateStage> _stages;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DailyUpdateOrchestrator(IDailyUpdateRunStore store, IEnumerable<IDailyUpdateStage> stages, TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(stages);
        _stages = stages.ToDictionary(stage => stage.Step);
        if (_stages.Count != OrderedSteps.Length || OrderedSteps.Any(step => !_stages.ContainsKey(step)))
            throw new ArgumentException("Every one of the eleven daily-update stages must be configured exactly once.", nameof(stages));
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<DailyUpdateRunResult> RunAsync(DailyUpdateRequest request, IProgress<DailyUpdateStepProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (request.EvaluationBarDate == default || request.RequestedAtUtc.Kind != DateTimeKind.Utc || string.IsNullOrWhiteSpace(request.UniverseDefinitionHash))
            throw new ArgumentException("The evaluation date, UTC request timestamp, and universe definition hash are required.", nameof(request));
        if (!await _gate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("A daily update is already running.");
        try
        {
            var startedAtUtc = _clock.GetUtcNow().UtcDateTime;
            var run = await _store.StartAsync(startedAtUtc, cancellationToken);
            var context = new DailyUpdateContext(run.DailyUpdateRunId, request, startedAtUtc);
            var outcomes = new Dictionary<DailyUpdateStep, DailyUpdateStepResult>();
            try
            {
                foreach (var step in OrderedSteps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    context.SetStageProgressReporter((detail, completedWorkItems, totalWorkItems) =>
                        progress?.Report(CreateInProgressProgress(run.DailyUpdateRunId, step, outcomes, detail, completedWorkItems, totalWorkItems)));
                    progress?.Report(CreateInProgressProgress(run.DailyUpdateRunId, step, outcomes,
                        $"ステップ {outcomes.Count + 1}/{OrderedSteps.Length}: {DisplayStep(step)} を開始しています。"));
                    var outcome = await ExecuteStepSafelyAsync(_stages[step], context, cancellationToken);
                    context.SetStageProgressReporter(null);
                    outcomes.Add(step, outcome);
                    if (step == DailyUpdateStep.RefreshMarketData) context.AnalyzedAtUtc = _clock.GetUtcNow().UtcDateTime;
                    var summary = Serialize(outcomes, isCoreComplete: outcomes.Count >= 9);
                    await _store.UpdateSummaryAsync(run.DailyUpdateRunId, summary, cancellationToken);
                    progress?.Report(CreateProgress(run.DailyUpdateRunId, step, outcomes));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var summary = Serialize(outcomes, isCoreComplete: false);
                await _store.CompleteAsync(run.DailyUpdateRunId, "Failed", _clock.GetUtcNow().UtcDateTime, summary, CancellationToken.None);
                throw;
            }

            var status = CoreStatus(outcomes);
            var completed = _clock.GetUtcNow().UtcDateTime;
            var finalSummary = Serialize(outcomes, isCoreComplete: true);
            await _store.CompleteAsync(run.DailyUpdateRunId, status, completed, finalSummary, cancellationToken);
            return new DailyUpdateRunResult(run.DailyUpdateRunId, status, outcomes);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<DailyUpdateStepResult> ExecuteStepSafelyAsync(IDailyUpdateStage stage, DailyUpdateContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await stage.ExecuteAsync(context, cancellationToken) ?? new DailyUpdateStepResult(0, 1, "The stage returned no outcome.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            var detail = exception.Message.Replace('\r', ' ').Replace('\n', ' ');
            return new DailyUpdateStepResult(0, 1, detail.Length <= 1000 ? detail : detail[..1000]);
        }
    }

    private static DailyUpdateStepProgress CreateProgress(int runId, DailyUpdateStep step, IReadOnlyDictionary<DailyUpdateStep, DailyUpdateStepResult> outcomes) => new(
        runId, step, outcomes.Count, OrderedSteps.Length, outcomes.Values.Sum(outcome => outcome.SucceededCount), outcomes.Values.Sum(outcome => outcome.FailedCount),
        outcomes[step].IsSuccess ? "Succeeded" : "Failed", outcomes[step].Detail);

    private static DailyUpdateStepProgress CreateInProgressProgress(int runId, DailyUpdateStep step, IReadOnlyDictionary<DailyUpdateStep, DailyUpdateStepResult> outcomes,
        string detail, int? completedWorkItems = null, int? totalWorkItems = null) => new(
        runId, step, outcomes.Count, OrderedSteps.Length, outcomes.Values.Sum(outcome => outcome.SucceededCount), outcomes.Values.Sum(outcome => outcome.FailedCount),
        "Running", detail, completedWorkItems, totalWorkItems);

    private static string DisplayStep(DailyUpdateStep step) => step switch
    {
        DailyUpdateStep.RefreshMarketData => "外部データを更新",
        DailyUpdateStep.VerifyDataAvailability => "データ利用可否を確認",
        DailyUpdateStep.ApplyCorporateActionAdjustments => "企業アクションを反映",
        DailyUpdateStep.BuildPointInTimeSeries => "時点整合データを準備",
        DailyUpdateStep.RunTechnicalAnalysis => "テクニカル分析を実行",
        DailyUpdateStep.ExtractLongCandidates => "Long候補を抽出",
        DailyUpdateStep.ExtractShortCandidates => "Short候補を抽出",
        DailyUpdateStep.ReevaluateHoldings => "保有を再評価",
        DailyUpdateStep.PersistAnalysisResults => "分析結果を保存",
        DailyUpdateStep.PublishResults => "結果を公開",
        DailyUpdateStep.EnqueueAiChecks => "AIチェックをキューへ投入",
        _ => step.ToString(),
    };

    private static string CoreStatus(IReadOnlyDictionary<DailyUpdateStep, DailyUpdateStepResult> outcomes)
    {
        var core = OrderedSteps.Take(9).Select(step => outcomes[step]).ToArray();
        return core.All(outcome => outcome.FailedCount == 0) ? "Succeeded" : core.All(outcome => outcome.SucceededCount == 0) ? "Failed" : "PartiallySucceeded";
    }

    private static string Serialize(IReadOnlyDictionary<DailyUpdateStep, DailyUpdateStepResult> outcomes, bool isCoreComplete) => JsonSerializer.Serialize(new
    {
        schemaVersion = "daily-update-summary-v1",
        coreAnalysisComplete = isCoreComplete,
        steps = outcomes.OrderBy(item => item.Key).Select(item => new { step = item.Key.ToString(), succeeded = item.Value.SucceededCount, failed = item.Value.FailedCount, detail = item.Value.Detail }),
    });
}
