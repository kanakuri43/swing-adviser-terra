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
        var series = await BuildSeriesBatchAsync([instrumentId], date, analyzedAtUtc, required, cancellationToken);
        return series.Single();
    }

    public async Task<IReadOnlyList<PointInTimeAnalysisSeries>> BuildSeriesBatchAsync(IReadOnlyList<int> instrumentIds, DateOnly date, DateTime analyzedAtUtc, int required, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var ids = instrumentIds.Distinct().ToArray();
        if (ids.Length == 0) return Array.Empty<PointInTimeAnalysisSeries>();
        var historyStart = TechnicalHistoryWindow.GetStart(date, required, _historyLookbackYears);
        var allBars = await _context.DailyBars.AsNoTracking()
            .Where(item => ids.Contains(item.InstrumentId) && item.TradingDate >= historyStart && item.TradingDate <= date && item.FetchedAtUtc <= analyzedAtUtc)
            .ToListAsync(cancellationToken);
        var allActions = await _context.CorporateActions.AsNoTracking()
            .Where(item => ids.Contains(item.InstrumentId) && item.EffectiveDate >= historyStart && item.EffectiveDate <= date && item.AvailableAtUtc <= analyzedAtUtc)
            .ToListAsync(cancellationToken);
        var prepared = ids.Select(instrumentId => PrepareSeries(
            instrumentId,
            date,
            required,
            historyStart,
            allBars.Where(item => item.InstrumentId == instrumentId).GroupBy(item => item.TradingDate).Select(group => group.OrderByDescending(item => item.Revision).First()).OrderBy(item => item.TradingDate).ToArray(),
            allActions.Where(item => item.InstrumentId == instrumentId).GroupBy(item => item.SourceEventId).Select(group => group.OrderByDescending(item => item.Revision).First()).OrderBy(item => item.EffectiveDate).ThenBy(item => item.SourceEventId, StringComparer.Ordinal).ToArray()))
            .ToArray();

        var existing = await _context.AnalysisInputManifests.AsNoTracking()
            .Where(item => ids.Contains(item.InstrumentId) && item.EvaluationBarDate == date)
            .OrderByDescending(item => item.AnalyzedAtUtc).ThenByDescending(item => item.ManifestId)
            .ToListAsync(cancellationToken);
        var manifests = existing
            .GroupBy(item => (item.InstrumentId, item.ManifestHash))
            .ToDictionary(group => group.Key, group => group.First());
        var missing = prepared.Where(item => !manifests.ContainsKey((item.InstrumentId, item.ManifestHash))).ToArray();
        if (missing.Length != 0)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var newManifests = missing.Select(item => new AnalysisInputManifest { InstrumentId = item.InstrumentId, EvaluationBarDate = date, AnalyzedAtUtc = analyzedAtUtc, FirstBarDate = item.FirstBarDate, LastBarDate = item.LastBarDate, BarCount = item.Bars.Length, PriceRevisionSetHash = item.PriceHash, CorporateActionSetHash = item.ActionHash, ManifestHash = item.ManifestHash, CreatedAtUtc = analyzedAtUtc }).ToArray();
                _context.AnalysisInputManifests.AddRange(newManifests);
                await _context.SaveChangesAsync(cancellationToken);
                foreach (var manifest in newManifests) manifests[(manifest.InstrumentId, manifest.ManifestHash)] = manifest;
                _context.AddRange(missing.SelectMany(item => item.Bars.Select(bar => new AnalysisInputManifestBar { ManifestId = manifests[(item.InstrumentId, item.ManifestHash)].ManifestId, TradingDate = bar.TradingDate, DailyBarId = bar.DailyBarId })));
                _context.AddRange(missing.SelectMany(item => item.Actions.Select(action => new AnalysisInputManifestCorporateAction { ManifestId = manifests[(item.InstrumentId, item.ManifestHash)].ManifestId, CorporateActionId = action.CorporateActionId })));
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _context.ChangeTracker.Clear();
                throw;
            }
        }
        try
        {
            return prepared.Select(item => new PointInTimeAnalysisSeries(manifests[(item.InstrumentId, item.ManifestHash)].ManifestId, item.InstrumentId, date, analyzedAtUtc, item.ManifestHash, item.PriceHash, item.ActionHash, item.AdjustedBars, item.Status, required)).ToArray();
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    public async Task<int> SaveIndicatorAsync(int scanRunId, int snapshotId, PointInTimeAnalysisSeries series, IndicatorComputation value, CancellationToken cancellationToken)
    {
        var entity = CreateIndicatorResult(scanRunId, snapshotId, series, value);
        _context.IndicatorResults.Add(entity); await _context.SaveChangesAsync(cancellationToken); return entity.IndicatorResultId;
    }

    public async Task<ReusableTechnicalScanResult?> FindReusableResultAsync(string manifestHash, int strategySnapshotId, CancellationToken cancellationToken)
    {
        var result = await _context.IndicatorResults.AsNoTracking()
            .Include(item => item.CandidateResults)
            .Where(item => item.Manifest.ManifestHash == manifestHash && item.StrategyParameterSnapshotId == strategySnapshotId
                && (item.ScanRun.Status == "Succeeded" || item.ScanRun.Status == "PartiallySucceeded"))
            .OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.IndicatorResultId)
            .FirstOrDefaultAsync(cancellationToken);
        if (result is null) return null;
        if (result.DataStatus != "Ok")
        {
            return new ReusableTechnicalScanResult(result.IndicatorResultId, result.DataStatus, result.HistoryAvailableCount, result.HistoryRequiredCount, 0);
        }

        var candidates = result.CandidateResults;
        if (candidates.Count != 2
            || candidates.Any(candidate => candidate.CandidateScoringEngineVersion != TechnicalStrategyParameters.CandidateEngineVersion)
            || candidates.Select(candidate => candidate.Direction).Distinct(StringComparer.Ordinal).Count() != 2
            || !candidates.Any(candidate => candidate.Direction == "Long")
            || !candidates.Any(candidate => candidate.Direction == "Short"))
        {
            return null;
        }
        return new ReusableTechnicalScanResult(result.IndicatorResultId, result.DataStatus, result.HistoryAvailableCount, result.HistoryRequiredCount, candidates.Count(candidate => candidate.Matched));
    }

    public async Task SaveBatchAsync(int scanRunId, int strategySnapshotId, IReadOnlyList<TechnicalScanPersistenceItem> items, DateTime createdAtUtc, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return;
        try
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case ComputedTechnicalScanPersistenceItem computed:
                    {
                        var indicator = CreateIndicatorResult(scanRunId, strategySnapshotId, computed.Series, computed.Indicator);
                        _context.IndicatorResults.Add(indicator);
                        _context.ScanRunResultUses.Add(new ScanRunResultUse { ScanRunId = scanRunId, IndicatorResult = indicator, UseKind = "Computed", UsedAtUtc = createdAtUtc });
                        if (computed.Indicator.DataStatus == "Ok")
                        {
                            foreach (var candidate in computed.Candidates)
                            {
                                indicator.CandidateResults.Add(CreateCandidateResult(item.InstrumentId, candidate, createdAtUtc));
                            }
                        }
                        else
                        {
                            AddExclusion(scanRunId, item.InstrumentId, computed.Indicator.DataStatus, computed.Indicator.HistoryAvailableCount, computed.Indicator.HistoryRequiredCount);
                        }
                        break;
                    }
                    case ReusedTechnicalScanPersistenceItem reused:
                        _context.ScanRunResultUses.Add(new ScanRunResultUse { ScanRunId = scanRunId, IndicatorResultId = reused.Result.IndicatorResultId, UseKind = "Reused", UsedAtUtc = createdAtUtc });
                        if (reused.Result.DataStatus != "Ok")
                        {
                            AddExclusion(scanRunId, item.InstrumentId, reused.Result.DataStatus, reused.Result.HistoryAvailableCount, reused.Result.HistoryRequiredCount);
                        }
                        break;
                    case FailedTechnicalScanPersistenceItem failed:
                        AddExclusion(scanRunId, item.InstrumentId, failed.Reason, failed.HistoryAvailableCount, failed.HistoryRequiredCount);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported scan persistence item: {item.GetType().Name}.");
                }
            }
            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            // A batch may contain full analysis manifests through navigation fix-up. Do not retain
            // them for the next batch, or long scans grow progressively slower.
            _context.ChangeTracker.Clear();
        }
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

    private void AddExclusion(int scanRunId, int instrumentId, string reason, int available, int required) =>
        _context.ScanExclusions.Add(new ScanExclusion { ScanRunId = scanRunId, InstrumentId = instrumentId, Reason = reason, HistoryAvailableCount = available, HistoryRequiredCount = required });

    private static IndicatorResult CreateIndicatorResult(int scanRunId, int snapshotId, PointInTimeAnalysisSeries series, IndicatorComputation value) =>
        new()
        {
            ScanRunId = scanRunId, InstrumentId = series.InstrumentId, EvaluationBarDate = series.EvaluationBarDate, AnalyzedAtUtc = series.AnalyzedAtUtc,
            ManifestId = series.ManifestId, StrategyParameterSnapshotId = snapshotId, DataStatus = value.DataStatus,
            HistoryAvailableCount = value.HistoryAvailableCount, HistoryRequiredCount = value.HistoryRequiredCount,
            MacdLine = value.MacdLine, MacdSignal = value.MacdSignal, MacdHistogram = value.MacdHistogram,
            Ema20 = value.Ema20, Ema50 = value.Ema50, Ema200 = value.Ema200, Atr14 = value.Atr14,
            VolumeRatio = value.VolumeRatio, VolumeReferenceAverage = value.VolumeReferenceAverage,
            VolumeRatioStatus = value.VolumeRatioStatus, RawValuesJson = value.RawValuesJson, CreatedAtUtc = series.AnalyzedAtUtc
        };

    private static CandidateResult CreateCandidateResult(int instrumentId, CandidateEvaluation value, DateTime createdAtUtc) =>
        new()
        {
            InstrumentId = instrumentId, Direction = value.Direction, SignalPurpose = "Entry", Matched = value.Matched,
            Score = value.Score, ConfidenceLabel = value.ConfidenceLabel,
            CandidateScoringEngineVersion = TechnicalStrategyParameters.CandidateEngineVersion,
            ScoreComponentsJson = value.ComponentsJson, CreatedAtUtc = createdAtUtc
        };

    private static PreparedAnalysisSeries PrepareSeries(int instrumentId, DateOnly date, int required, DateOnly historyStart, DailyBar[] bars, CorporateAction[] actions)
    {
        var status = DetermineStatus(bars, actions, date, required);
        var adjusted = status == "Ok" ? Adjust(bars, actions, date, out status) : Array.Empty<AdjustedDailyBar>();
        var priceHash = Hash(string.Join("\n", bars.Select(item => $"{item.TradingDate:yyyy-MM-dd}|{item.DailyBarId}")));
        var actionHash = Hash(string.Join("\n", actions.Select(item => $"{item.EffectiveDate:yyyy-MM-dd}|{item.SourceEventId}|{item.CorporateActionId}")));
        var first = bars.FirstOrDefault()?.TradingDate ?? date;
        var last = bars.LastOrDefault()?.TradingDate ?? date;
        // `analyzedAtUtc` determines which revisions were eligible, but is not an input
        // revision. An identical selected set can use the same frozen manifest later.
        var manifestHash = Hash(JsonSerializer.Serialize(new { schema = "analysis-input-manifest-v2", instrumentId, date, historyStart, first, last, count = bars.Length, priceHash, actionHash, selection = "pit-revision-finite-window-v2" }));
        return new PreparedAnalysisSeries(instrumentId, bars, actions, first, last, status, adjusted, priceHash, actionHash, manifestHash);
    }

    private sealed record PreparedAnalysisSeries(
        int InstrumentId,
        DailyBar[] Bars,
        CorporateAction[] Actions,
        DateOnly FirstBarDate,
        DateOnly LastBarDate,
        string Status,
        AdjustedDailyBar[] AdjustedBars,
        string PriceHash,
        string ActionHash,
        string ManifestHash);

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
