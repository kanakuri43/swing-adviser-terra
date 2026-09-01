using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.DailyUpdates;
using SwingAdviser.Infrastructure.MarketData;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.DailyUpdates;

/// <summary>Runs independently auditable master, margin, price, action, and fundamental refreshes before analysis.</summary>
public sealed class MarketDataDailyUpdateStage(MarketDataIngestionService ingestion, SwingAdviserDbContext context, int historyLookbackYears, int requiredHistoryCount) : IDailyUpdateStage
{
    public DailyUpdateStep Step => DailyUpdateStep.RefreshMarketData;

    public async Task<DailyUpdateStepResult> ExecuteAsync(DailyUpdateContext update, CancellationToken cancellationToken)
    {
        update.ReportStageProgress("ステップ 1/11: JPXの上場銘柄一覧を取得しています。ネットワーク応答を待機中です。");
        await ingestion.RefreshInstrumentMasterAsync(update.DailyUpdateRunId, cancellationToken);
        update.ReportStageProgress("ステップ 1/11: JPXの信用・貸借銘柄一覧を取得しています。ネットワーク応答を待機中です。");
        await ingestion.RefreshMarginEligibilityAsync(update.DailyUpdateRunId, cancellationToken);
        var universe = await LatestEligibleUniverseAsync(cancellationToken);
        var historyStart = TechnicalHistoryWindow.GetStart(update.Request.EvaluationBarDate, requiredHistoryCount, historyLookbackYears);
        var refreshTargets = await ingestion.CreateRefreshTargetsAsync(
            universe,
            update.Request.EvaluationBarDate,
            update.AnalyzedAtUtc,
            DateTime.SpecifyKind(historyStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            cancellationToken);
        var chartRequestCount = refreshTargets.Count(target => target.RefreshChart);
        var cacheHitCount = refreshTargets.Count - chartRequestCount;
        update.ReportStageProgress($"ステップ 1/11: {universe.Count:n0}銘柄を確認しています（キャッシュ利用 {cacheHitCount:n0}、不足分 {chartRequestCount:n0}件を過去{historyLookbackYears}年で取得）。", 0, universe.Count);
        var instrumentProgress = new Progress<InstrumentRefreshProgress>(item => update.ReportStageProgress(
            $"ステップ 1/11: 株価を{(item.UsedCachedChart ? "キャッシュから確認" : "取得") }中（{item.CompletedCount:n0}/{item.TotalCount:n0}銘柄、直近: {item.LastCompletedCode}）。中止できます。",
            item.CompletedCount, item.TotalCount));
        await ingestion.RefreshInstrumentsAsync(update.DailyUpdateRunId, refreshTargets, cancellationToken, instrumentProgress);
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
        return new DailyUpdateStepResult(fetches.Count(fetch => fetch.Status == "Succeeded"), fetches.Count(fetch => fetch.Status == "Failed"), $"Refreshed {universe.Count} instruments; every source attempt is retained in external_fetch_results.");
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
