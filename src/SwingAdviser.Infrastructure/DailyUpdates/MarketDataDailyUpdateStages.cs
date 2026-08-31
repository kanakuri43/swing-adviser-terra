using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.DailyUpdates;
using SwingAdviser.Infrastructure.MarketData;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.DailyUpdates;

/// <summary>Runs independently auditable master, margin, price, action, and fundamental refreshes before analysis.</summary>
public sealed class MarketDataDailyUpdateStage(MarketDataIngestionService ingestion, SwingAdviserDbContext context) : IDailyUpdateStage
{
    public DailyUpdateStep Step => DailyUpdateStep.RefreshMarketData;

    public async Task<DailyUpdateStepResult> ExecuteAsync(DailyUpdateContext update, CancellationToken cancellationToken)
    {
        await ingestion.RefreshInstrumentMasterAsync(update.DailyUpdateRunId, cancellationToken);
        await ingestion.RefreshMarginEligibilityAsync(update.DailyUpdateRunId, cancellationToken);
        var universe = await LatestEligibleUniverseAsync(cancellationToken);
        await ingestion.RefreshInstrumentsAsync(update.DailyUpdateRunId, universe, cancellationToken);
        // A backwardation source is intentionally not fabricated. Its absence remains visible rather than becoming a zero-cost assumption.
        context.ExternalFetchResults.Add(new SwingAdviser.Domain.Analysis.ExternalFetchResult
        {
            DailyUpdateRunId = update.DailyUpdateRunId,
            SourceKind = "Backwardation",
            Status = "Failed",
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
