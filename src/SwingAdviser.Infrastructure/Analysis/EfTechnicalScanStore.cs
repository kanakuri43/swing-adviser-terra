using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Analysis;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.Persistence;
using SwingAdviser.Infrastructure.MarketData;

namespace SwingAdviser.Infrastructure.Analysis;

/// <summary>EF Core adapter that freezes point-in-time inputs before the Domain engine receives them.</summary>
public sealed class EfTechnicalScanStore : ITechnicalScanStore
{
    private readonly SwingAdviserDbContext _context;
    private readonly int _historyLookbackYears;

    public EfTechnicalScanStore(SwingAdviserDbContext context, int historyLookbackYears = 5)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _historyLookbackYears = Math.Clamp(historyLookbackYears, 1, 10);
    }

    public async Task<IReadOnlyList<TechnicalScanInstrument>> GetEligibleUniverseAsync(DateOnly date, DateTime analyzedAtUtc, CancellationToken cancellationToken)
    {
        var revisions = await _context.InstrumentMasterRevisions.Where(item => item.EffectiveAtDate <= date && item.AvailableAtUtc <= analyzedAtUtc && item.RecordedAtUtc <= analyzedAtUtc).ToListAsync(cancellationToken);
        return revisions.GroupBy(item => item.InstrumentId).Select(group => group.OrderByDescending(item => item.Revision).First())
            .Where(item => item.MarketSegment is "Prime" or "Standard" or "Growth" && item.InstrumentType == "DomesticCommonStock" && item.ListedStatus == "Listed" && item.ScanEligibility == "Eligible" && item.Status == "Active")
            .OrderBy(item => item.Code, StringComparer.Ordinal).Select(item => new TechnicalScanInstrument(item.InstrumentId, item.Code)).ToArray();
    }

    public async Task<int> GetOrCreateStrategySnapshotAsync(TechnicalStrategyParameters parameters, DateTime createdAtUtc, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(parameters); var hash = Hash(json);
        var existing = await _context.StrategyParameterSnapshots.SingleOrDefaultAsync(item => item.ContentSha256 == hash, cancellationToken);
        if (existing is not null) return existing.StrategyParameterSnapshotId;
        var entity = new StrategyParameterSnapshot { StrategyKey = TechnicalStrategyParameters.StrategyKey, StrategyVersion = TechnicalStrategyParameters.StrategyVersion, IndicatorEngineVersion = TechnicalStrategyParameters.IndicatorEngineVersion, CandidateScoringEngineVersion = TechnicalStrategyParameters.CandidateEngineVersion, NormalizedParametersJson = json, ContentSha256 = hash, CreatedAtUtc = createdAtUtc };
        _context.StrategyParameterSnapshots.Add(entity); await _context.SaveChangesAsync(cancellationToken); return entity.StrategyParameterSnapshotId;
    }

    public async Task<int> CreateScanRunAsync(TechnicalScanRequest request, int totalInstruments, CancellationToken cancellationToken)
    {
        var run = new ScanRun { DailyUpdateRunId = request.DailyUpdateRunId, RunType = "TechnicalAnalysis", UniverseDefinitionHash = request.UniverseDefinitionHash, StartedAtUtc = request.AnalyzedAtUtc, Status = "Running", TotalInstruments = totalInstruments };
        _context.ScanRuns.Add(run); await _context.SaveChangesAsync(cancellationToken); return run.ScanRunId;
    }

    public async Task<PointInTimeAnalysisSeries> BuildSeriesAsync(int instrumentId, DateOnly date, DateTime analyzedAtUtc, int required, CancellationToken cancellationToken)
    {
        var historyStart = TechnicalHistoryWindow.GetStart(date, required, _historyLookbackYears);
        var allBars = await _context.DailyBars.Where(item => item.InstrumentId == instrumentId && item.TradingDate >= historyStart && item.TradingDate <= date && item.FetchedAtUtc <= analyzedAtUtc).ToListAsync(cancellationToken);
        var bars = allBars.GroupBy(item => item.TradingDate).Select(group => group.OrderByDescending(item => item.Revision).First()).OrderBy(item => item.TradingDate).ToArray();
        var actions = (await _context.CorporateActions.Where(item => item.InstrumentId == instrumentId && item.EffectiveDate >= historyStart && item.EffectiveDate <= date && item.AvailableAtUtc <= analyzedAtUtc).ToListAsync(cancellationToken))
            .GroupBy(item => item.SourceEventId).Select(group => group.OrderByDescending(item => item.Revision).First()).OrderBy(item => item.EffectiveDate).ThenBy(item => item.SourceEventId, StringComparer.Ordinal).ToArray();
        var status = DetermineStatus(bars, actions, date, required);
        var adjusted = status == "Ok" ? Adjust(bars, actions, date, out status) : Array.Empty<AdjustedDailyBar>();
        var priceHash = Hash(string.Join("\n", bars.Select(item => $"{item.TradingDate:yyyy-MM-dd}|{item.DailyBarId}")));
        var actionHash = Hash(string.Join("\n", actions.Select(item => $"{item.EffectiveDate:yyyy-MM-dd}|{item.SourceEventId}|{item.CorporateActionId}")));
        var first = bars.FirstOrDefault()?.TradingDate ?? date; var last = bars.LastOrDefault()?.TradingDate ?? date;
        var manifestHash = Hash(JsonSerializer.Serialize(new { schema = "analysis-input-manifest-v2", instrumentId, date, analyzedAtUtc, historyStart, first, last, count = bars.Length, priceHash, actionHash, selection = "pit-revision-finite-window-v2" }));
        var manifest = await _context.AnalysisInputManifests.SingleOrDefaultAsync(item => item.InstrumentId == instrumentId && item.EvaluationBarDate == date && item.AnalyzedAtUtc == analyzedAtUtc && item.ManifestHash == manifestHash, cancellationToken);
        if (manifest is null)
        {
            manifest = new AnalysisInputManifest { InstrumentId = instrumentId, EvaluationBarDate = date, AnalyzedAtUtc = analyzedAtUtc, FirstBarDate = first, LastBarDate = last, BarCount = bars.Length, PriceRevisionSetHash = priceHash, CorporateActionSetHash = actionHash, ManifestHash = manifestHash, CreatedAtUtc = analyzedAtUtc };
            _context.AnalysisInputManifests.Add(manifest); await _context.SaveChangesAsync(cancellationToken);
            _context.AddRange(bars.Select(item => new AnalysisInputManifestBar { ManifestId = manifest.ManifestId, TradingDate = item.TradingDate, DailyBarId = item.DailyBarId }));
            _context.AddRange(actions.Select(item => new AnalysisInputManifestCorporateAction { ManifestId = manifest.ManifestId, CorporateActionId = item.CorporateActionId }));
            await _context.SaveChangesAsync(cancellationToken);
        }
        return new PointInTimeAnalysisSeries(manifest.ManifestId, instrumentId, date, analyzedAtUtc, manifestHash, priceHash, actionHash, adjusted, status, required);
    }

    public async Task<int> SaveIndicatorAsync(int scanRunId, int snapshotId, PointInTimeAnalysisSeries series, IndicatorComputation value, CancellationToken cancellationToken)
    {
        var entity = new IndicatorResult { ScanRunId = scanRunId, InstrumentId = series.InstrumentId, EvaluationBarDate = series.EvaluationBarDate, AnalyzedAtUtc = series.AnalyzedAtUtc, ManifestId = series.ManifestId, StrategyParameterSnapshotId = snapshotId, DataStatus = value.DataStatus, HistoryAvailableCount = value.HistoryAvailableCount, HistoryRequiredCount = value.HistoryRequiredCount, MacdLine = value.MacdLine, MacdSignal = value.MacdSignal, MacdHistogram = value.MacdHistogram, Ema20 = value.Ema20, Ema50 = value.Ema50, Ema200 = value.Ema200, Atr14 = value.Atr14, VolumeRatio = value.VolumeRatio, VolumeReferenceAverage = value.VolumeReferenceAverage, VolumeRatioStatus = value.VolumeRatioStatus, RawValuesJson = value.RawValuesJson, CreatedAtUtc = series.AnalyzedAtUtc };
        _context.IndicatorResults.Add(entity); await _context.SaveChangesAsync(cancellationToken); return entity.IndicatorResultId;
    }
    public async Task SaveCandidatesAsync(int indicatorId, int instrumentId, IReadOnlyList<CandidateEvaluation> candidates, DateTime createdAtUtc, CancellationToken cancellationToken)
    {
        try
        {
            _context.CandidateResults.AddRange(candidates.Select(item => new CandidateResult { IndicatorResultId = indicatorId, InstrumentId = instrumentId, Direction = item.Direction, SignalPurpose = "Entry", Matched = item.Matched, Score = item.Score, ConfidenceLabel = item.ConfidenceLabel, CandidateScoringEngineVersion = TechnicalStrategyParameters.CandidateEngineVersion, ScoreComponentsJson = item.ComponentsJson, CreatedAtUtc = createdAtUtc }));
            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            // The manifest contains hundreds to thousands of bars. Retaining every processed
            // instrument in one long-lived context makes later scan iterations progressively slower.
            _context.ChangeTracker.Clear();
        }
    }
    public async Task SaveExclusionAsync(int runId, int instrumentId, string reason, int available, int required, CancellationToken cancellationToken)
    {
        try
        {
            _context.ScanExclusions.Add(new ScanExclusion { ScanRunId = runId, InstrumentId = instrumentId, Reason = reason, HistoryAvailableCount = available, HistoryRequiredCount = required });
            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }
    public async Task CompleteScanRunAsync(int runId, string status, int succeeded, int failed, DateTime completed, CancellationToken cancellationToken)
    {
        var entity = await _context.ScanRuns.SingleAsync(item => item.ScanRunId == runId, cancellationToken); entity.Status = status; entity.SucceededCount = succeeded; entity.FailedCount = failed; entity.CompletedAtUtc = completed; await _context.SaveChangesAsync(cancellationToken);
    }

    private static string DetermineStatus(DailyBar[] bars, CorporateAction[] actions, DateOnly date, int required)
    {
        if (bars.Length < required) return "InsufficientHistory";
        if (bars[^1].TradingDate != date || bars.Any(item => item.Status is not ("Final" or "Corrected"))) return "InvalidData";
        if (actions.Any(item => item.Status == "PointInTimeUnverified")) return "PointInTimeUnverified";
        if (actions.Any(item => item.Status == "ReconciliationRequired" || item.ActionType == "Unsupported")) return "ReconciliationRequired";
        if (actions.Any(item => item.Status != "Active")) return "InvalidData";
        return "Ok";
    }
    private static AdjustedDailyBar[] Adjust(DailyBar[] bars, CorporateAction[] actions, DateOnly date, out string status)
    {
        var result = bars.Select(item => new AdjustedDailyBar(item.DailyBarId, item.TradingDate, item.Open, item.High, item.Low, item.Close, item.Volume)).ToArray(); status = "Ok";
        foreach (var action in actions)
        {
            if (action.ActionType == "Split")
            {
                if (action.SplitRatioNumerator is null or <= 0 || action.SplitRatioDenominator is null or <= 0) { status = "InvalidData"; return Array.Empty<AdjustedDailyBar>(); }
                var factor = (decimal)action.SplitRatioDenominator.Value / action.SplitRatioNumerator.Value; var volumeFactor = (decimal)action.SplitRatioNumerator.Value / action.SplitRatioDenominator.Value;
                result = result.Select(bar => bar.TradingDate < action.EffectiveDate ? bar with { Open = bar.Open * factor, High = bar.High * factor, Low = bar.Low * factor, Close = bar.Close * factor, Volume = checked((long)(bar.Volume * volumeFactor)) } : bar).ToArray();
            }
            else if (action.ActionType == "CashDividend")
            {
                var prior = result.LastOrDefault(bar => bar.TradingDate < action.EffectiveDate); if (prior is null || action.DividendAmountPerShare is null || action.Currency != "JPY" || prior.Close <= action.DividendAmountPerShare.Value) { status = "InvalidData"; return Array.Empty<AdjustedDailyBar>(); }
                var factor = (prior.Close - action.DividendAmountPerShare.Value) / prior.Close; result = result.Select(bar => bar.TradingDate < action.EffectiveDate ? bar with { Open = bar.Open * factor, High = bar.High * factor, Low = bar.Low * factor, Close = bar.Close * factor } : bar).ToArray();
            }
            else { status = "ReconciliationRequired"; return Array.Empty<AdjustedDailyBar>(); }
        }
        return result;
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
