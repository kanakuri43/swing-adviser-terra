using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.DailyUpdates;
using SwingAdviser.Infrastructure.MarketData;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.DailyUpdates;

/// <summary>Runs independently auditable master, margin, price, action, and fundamental refreshes before analysis.</summary>
public sealed class MarketDataDailyUpdateStage(MarketDataIngestionService ingestion, SwingAdviserDbContext context, int historyLookbackYears, int requiredHistoryCount,
    TimeSpan checkpointValidity) : IDailyUpdateStage
{
    public DailyUpdateStep Step => DailyUpdateStep.RefreshMarketData;

    public async Task<DailyUpdateStepResult> ExecuteAsync(DailyUpdateContext update, CancellationToken cancellationToken)
    {
        var repository = new MarketDataRepository(context);
        var historyStart = TechnicalHistoryWindow.GetStart(update.Request.EvaluationBarDate, requiredHistoryCount, historyLookbackYears);
        var historyStartUtc = DateTime.SpecifyKind(historyStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        update.ReportStageProgress("ステップ 1/11: JPXの上場銘柄一覧を取得しています。ネットワーク応答を待機中です。");
        await RefreshGlobalSourceAsync(repository, update, "JPX-ListedIssues", () => ingestion.RefreshInstrumentMasterAsync(update.DailyUpdateRunId, cancellationToken), historyStart, cancellationToken);
        update.ReportStageProgress("ステップ 1/11: JPXの信用・貸借銘柄一覧を取得しています。ネットワーク応答を待機中です。");
        await RefreshGlobalSourceAsync(repository, update, "JPX-MarginIssues", () => ingestion.RefreshMarginEligibilityAsync(update.DailyUpdateRunId, cancellationToken), historyStart, cancellationToken);
        var universe = await LatestEligibleUniverseAsync(cancellationToken);
        var refreshTargets = await ingestion.CreateRefreshTargetsAsync(
            universe,
            update.Request.EvaluationBarDate,
            update.AnalyzedAtUtc,
            historyStartUtc,
            cancellationToken);
        var plannedTargets = new List<(InstrumentRefreshTarget Target, int? CheckpointId)>();
        update.ReportStageProgress($"ステップ 1/11: {refreshTargets.Count:n0}銘柄の更新計画と再開可能な取得結果を確認しています。", 0, refreshTargets.Count);
        var plannedCount = 0;
        foreach (var target in refreshTargets)
        {
            try
            {
                var decision = await repository.GetFetchCheckpointDecisionAsync(update.Request.EvaluationBarDate, "YahooFinanceChartApiV8", target.InstrumentId,
                    target.InstrumentId.ToString(), null, DateTime.UtcNow, cancellationToken);
                string? fingerprint = null;
                if (decision.CanReuse)
                {
                    fingerprint = await repository.GetSourceFingerprintAsync("YahooFinanceChartApiV8", target.InstrumentId, update.Request.EvaluationBarDate, historyStart, cancellationToken);
                    decision = await repository.GetFetchCheckpointDecisionAsync(update.Request.EvaluationBarDate, "YahooFinanceChartApiV8", target.InstrumentId,
                        target.InstrumentId.ToString(), fingerprint, DateTime.UtcNow, cancellationToken);
                }
                if (!target.RefreshChart && decision.CanReuse)
                {
                    var reused = await repository.RecordReusedCheckpointAsync(update.DailyUpdateRunId, update.Request.EvaluationBarDate, "YahooFinanceChartApiV8", target.InstrumentId,
                        target.InstrumentId.ToString(), decision.CheckpointId!.Value, fingerprint!, target.ChartPeriodStartUtc is null ? null : DateOnly.FromDateTime(target.ChartPeriodStartUtc.Value),
                        update.Request.EvaluationBarDate, DateTime.UtcNow, checkpointValidity, cancellationToken);
                    await repository.RecordFetchResultAsync(update.DailyUpdateRunId, "YahooFinanceChartApiV8", target.InstrumentId, "Reused", null,
                        "A valid same-evaluation-date checkpoint was reused.", 0, DateTime.UtcNow, cancellationToken);
                    await repository.AttachCheckpointToLatestFetchResultAsync(update.DailyUpdateRunId, "YahooFinanceChartApiV8", target.InstrumentId, reused.DailyUpdateFetchCheckpointId, cancellationToken);
                    plannedTargets.Add((target, reused.DailyUpdateFetchCheckpointId));
                    continue;
                }
                if (!target.RefreshChart && decision.Disposition == FetchCheckpointDisposition.Missing)
                {
                    // Existing verified cache from before this feature is safe to adopt, but is explicitly audited.
                    fingerprint = await repository.GetSourceFingerprintAsync("YahooFinanceChartApiV8", target.InstrumentId, update.Request.EvaluationBarDate, historyStart, cancellationToken);
                    var adopted = await repository.StartFetchCheckpointAsync(update.DailyUpdateRunId, update.Request.EvaluationBarDate, "YahooFinanceChartApiV8", target.InstrumentId,
                        target.InstrumentId.ToString(), target.ChartPeriodStartUtc is null ? null : DateOnly.FromDateTime(target.ChartPeriodStartUtc.Value), DateTime.UtcNow, checkpointValidity, "CacheValidated", cancellationToken);
                    await repository.CompleteFetchCheckpointAsync(adopted.DailyUpdateFetchCheckpointId, "Succeeded", fingerprint, update.Request.EvaluationBarDate, DateTime.UtcNow, cancellationToken);
                    await repository.RecordFetchResultAsync(update.DailyUpdateRunId, "YahooFinanceChartApiV8", target.InstrumentId, "Reused", null,
                        "A final cached chart and history window were validated for this evaluation date.", 0, DateTime.UtcNow, cancellationToken);
                    await repository.AttachCheckpointToLatestFetchResultAsync(update.DailyUpdateRunId, "YahooFinanceChartApiV8", target.InstrumentId, adopted.DailyUpdateFetchCheckpointId, cancellationToken);
                    plannedTargets.Add((target, adopted.DailyUpdateFetchCheckpointId));
                    continue;
                }
                var checkpoint = await repository.StartFetchCheckpointAsync(update.DailyUpdateRunId, update.Request.EvaluationBarDate, "YahooFinanceChartApiV8", target.InstrumentId,
                    target.InstrumentId.ToString(), target.ChartPeriodStartUtc is null ? null : DateOnly.FromDateTime(target.ChartPeriodStartUtc.Value), DateTime.UtcNow, checkpointValidity,
                    decision.Disposition == FetchCheckpointDisposition.Missing ? null : decision.Disposition.ToString(), cancellationToken);
                plannedTargets.Add((target with { RefreshChart = true }, checkpoint.DailyUpdateFetchCheckpointId));
            }
            finally
            {
                plannedCount++;
                if (plannedCount % 25 == 0 || plannedCount == refreshTargets.Count)
                    update.ReportStageProgress($"ステップ 1/11: {refreshTargets.Count:n0}銘柄の更新計画を確認中（{plannedCount:n0}/{refreshTargets.Count:n0}銘柄）。", plannedCount, refreshTargets.Count);
            }
        }
        var chartRequestCount = plannedTargets.Count(item => item.Target.RefreshChart);
        var cacheHitCount = plannedTargets.Count - chartRequestCount;
        update.ReportStageProgress($"ステップ 1/11: {universe.Count:n0}銘柄を確認しています（キャッシュ利用 {cacheHitCount:n0}、不足分 {chartRequestCount:n0}件を過去{historyLookbackYears}年で取得）。", 0, universe.Count);
        var instrumentProgress = new Progress<InstrumentRefreshProgress>(item => update.ReportStageProgress(
            $"ステップ 1/11: 株価データを{(item.UsedCachedChart ? "キャッシュから確認" : "取得・保存") }中（{item.CompletedCount:n0}/{item.TotalCount:n0}銘柄、取得成功 {item.SuccessfulCount:n0}件、取得失敗 {item.FailedCount:n0}件、キャッシュ確認 {item.ReusedCount:n0}件、直近: {item.LastCompletedCode}）。中止できます。",
            item.CompletedCount, item.TotalCount));
        await ingestion.RefreshInstrumentsAsync(update.DailyUpdateRunId, plannedTargets.Select(item => item.Target), cancellationToken, instrumentProgress);
        var finalizedCount = 0;
        var finalizedSucceededCount = 0;
        var finalizedFailedCount = 0;
        var finalizedUnknownCount = 0;
        update.ReportStageProgress($"ステップ 1/11: 株価取得結果を確定保存しています（0/{chartRequestCount:n0}銘柄）。", 0, chartRequestCount);
        foreach (var batch in plannedTargets.Where(item => item.Target.RefreshChart)
                     .Select(item => new FetchCheckpointFinalizationTarget(item.Target.InstrumentId, item.CheckpointId!.Value)).Chunk(100))
        {
            var finalized = await repository.FinalizeChartFetchCheckpointsAsync(update.DailyUpdateRunId, update.Request.EvaluationBarDate, historyStart,
                batch, DateTime.UtcNow, cancellationToken);
            foreach (var item in finalized)
            {
                finalizedCount++;
                if (item.FetchStatus == "Succeeded") finalizedSucceededCount++;
                else if (item.FetchStatus == "Failed") finalizedFailedCount++;
                else finalizedUnknownCount++;
            }
            update.ReportStageProgress($"ステップ 1/11: 株価取得結果を確定保存中（{finalizedCount:n0}/{chartRequestCount:n0}銘柄、取得成功 {finalizedSucceededCount:n0}件、取得失敗 {finalizedFailedCount:n0}件、結果未確認 {finalizedUnknownCount:n0}件）。中止できます。",
                finalizedCount, chartRequestCount);
        }
        // A backwardation source is intentionally not fabricated. Its absence remains visible rather than becoming a zero-cost assumption,
        // but does not turn the price/technical-analysis core into a false failure.
        context.ExternalFetchResults.Add(new SwingAdviser.Domain.Analysis.ExternalFetchResult
        {
            DailyUpdateRunId = update.DailyUpdateRunId,
            SourceKind = "Backwardation",
            Status = "Unavailable",
            ErrorKind = "NotImplemented",
            ErrorMessage = "No backwardation source is configured; margin costs remain reconciliation-required.",
            AttemptedAtUtc = DateTime.UtcNow,
        });
        await context.SaveChangesAsync(cancellationToken);
        var fetches = await context.ExternalFetchResults.Where(fetch => fetch.DailyUpdateRunId == update.DailyUpdateRunId).ToListAsync(cancellationToken);
        return new DailyUpdateStepResult(fetches.Count(fetch => fetch.Status is "Succeeded" or "Reused"), fetches.Count(fetch => fetch.Status == "Failed"), $"Refreshed {universe.Count} instruments; checkpoints, coverage, and every source attempt are retained.");
    }

    private async Task RefreshGlobalSourceAsync(MarketDataRepository repository, DailyUpdateContext update, string sourceKind, Func<Task<int>> refresh,
        DateOnly historyStart, CancellationToken cancellationToken)
    {
        const string targetKey = "global";
        var fingerprint = await repository.GetSourceFingerprintAsync(sourceKind, null, update.Request.EvaluationBarDate, historyStart, cancellationToken);
        var decision = await repository.GetFetchCheckpointDecisionAsync(update.Request.EvaluationBarDate, sourceKind, null, targetKey, fingerprint, DateTime.UtcNow, cancellationToken);
        if (decision.CanReuse)
        {
            var reused = await repository.RecordReusedCheckpointAsync(update.DailyUpdateRunId, update.Request.EvaluationBarDate, sourceKind, null, targetKey,
                decision.CheckpointId!.Value, fingerprint, null, null, DateTime.UtcNow, checkpointValidity, cancellationToken);
            await repository.RecordFetchResultAsync(update.DailyUpdateRunId, sourceKind, null, "Reused", null, "A valid same-evaluation-date checkpoint was reused.", 0, DateTime.UtcNow, cancellationToken);
            await repository.AttachCheckpointToLatestFetchResultAsync(update.DailyUpdateRunId, sourceKind, null, reused.DailyUpdateFetchCheckpointId, cancellationToken);
            return;
        }
        var checkpoint = await repository.StartFetchCheckpointAsync(update.DailyUpdateRunId, update.Request.EvaluationBarDate, sourceKind, null, targetKey, null,
            DateTime.UtcNow, checkpointValidity, decision.Disposition == FetchCheckpointDisposition.Missing ? null : decision.Disposition.ToString(), cancellationToken);
        await refresh();
        var fetch = await context.ExternalFetchResults.Where(result => result.DailyUpdateRunId == update.DailyUpdateRunId && result.SourceKind == sourceKind && result.InstrumentId == null)
            .OrderByDescending(result => result.FetchResultId).FirstOrDefaultAsync(cancellationToken);
        var completedFingerprint = fetch?.Status == "Succeeded"
            ? await repository.GetSourceFingerprintAsync(sourceKind, null, update.Request.EvaluationBarDate, historyStart, cancellationToken)
            : null;
        await repository.CompleteFetchCheckpointAsync(checkpoint.DailyUpdateFetchCheckpointId, fetch?.Status == "Succeeded" ? "Succeeded" : "Failed", completedFingerprint, null, DateTime.UtcNow, cancellationToken);
        await repository.AttachCheckpointToLatestFetchResultAsync(update.DailyUpdateRunId, sourceKind, null, checkpoint.DailyUpdateFetchCheckpointId, cancellationToken);
    }

    private async Task<IReadOnlyList<(int InstrumentId, string Code)>> LatestEligibleUniverseAsync(CancellationToken cancellationToken)
    {
        var revisions = await context.InstrumentMasterRevisions.Where(revision => revision.Status == "Active").ToListAsync(cancellationToken);
        return revisions.GroupBy(revision => revision.InstrumentId).Select(group => group.OrderByDescending(revision => revision.Revision).First())
            .Where(revision => revision.MarketSegment is "Prime" or "Standard" or "Growth" && revision.InstrumentType == "DomesticCommonStock" && revision.ListedStatus == "Listed" && revision.ScanEligibility == "Eligible")
            .OrderBy(revision => revision.Code, StringComparer.Ordinal).Select(revision => (revision.InstrumentId, revision.Code)).ToArray();
    }
}

/// <summary>Run-level visibility for finality, history coverage, and corporate-action availability. The scan repeats these checks per instrument and fails closed.</summary>
public sealed class DataAvailabilityVerificationDailyUpdateStage(SwingAdviserDbContext context) : IDailyUpdateStage
{
    public DailyUpdateStep Step => DailyUpdateStep.VerifyDataAvailability;

    public async Task<DailyUpdateStepResult> ExecuteAsync(DailyUpdateContext update, CancellationToken cancellationToken)
    {
        var eligible = await context.InstrumentMasterRevisions.Where(revision => revision.EffectiveAtDate <= update.Request.EvaluationBarDate && revision.AvailableAtUtc <= update.AnalyzedAtUtc && revision.Status == "Active").ToListAsync(cancellationToken);
        var current = eligible.GroupBy(revision => revision.InstrumentId).Select(group => group.OrderByDescending(revision => revision.Revision).First())
            .Where(revision => revision.MarketSegment is "Prime" or "Standard" or "Growth" && revision.InstrumentType == "DomesticCommonStock" && revision.ListedStatus == "Listed" && revision.ScanEligibility == "Eligible").ToArray();
        var ids = current.Select(revision => revision.InstrumentId).ToArray();
        var bars = await context.DailyBars.Where(bar => ids.Contains(bar.InstrumentId) && bar.TradingDate == update.Request.EvaluationBarDate && bar.FetchedAtUtc <= update.AnalyzedAtUtc).ToListAsync(cancellationToken);
        var valid = bars.GroupBy(bar => bar.InstrumentId).Count(group => group.OrderByDescending(bar => bar.Revision).First().Status is "Final" or "Corrected");
        return new DailyUpdateStepResult(valid, current.Length - valid, current.Length == valid ? "All eligible instruments have a final evaluation bar." : "Provisional or missing evaluation bars remain excluded by the technical scan.");
    }
}
