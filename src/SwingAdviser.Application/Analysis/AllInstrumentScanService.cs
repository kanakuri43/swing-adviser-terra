using SwingAdviser.Domain.Analysis;

namespace SwingAdviser.Application.Analysis;

/// <summary>Application service for deterministic, fail-closed technical candidate scanning.</summary>
public sealed class AllInstrumentScanService
{
    private readonly ITechnicalScanStore _store;
    private readonly TechnicalIndicatorEngine _indicatorEngine;
    private readonly CandidateScoringEngine _scoringEngine;

    public AllInstrumentScanService(ITechnicalScanStore store, TechnicalIndicatorEngine? indicatorEngine = null, CandidateScoringEngine? scoringEngine = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _indicatorEngine = indicatorEngine ?? new TechnicalIndicatorEngine();
        _scoringEngine = scoringEngine ?? new CandidateScoringEngine();
    }

    public async Task<TechnicalScanResult> RunAsync(TechnicalScanRequest request, IProgress<TechnicalScanProgress>? progress, CancellationToken cancellationToken)
    {
        request.Parameters.Validate();
        var universe = await _store.GetEligibleUniverseAsync(request.EvaluationBarDate, request.AnalyzedAtUtc, cancellationToken);
        var snapshotId = await _store.GetOrCreateStrategySnapshotAsync(request.Parameters, request.AnalyzedAtUtc, cancellationToken);
        var scanRunId = await _store.CreateScanRunAsync(request, universe.Count, cancellationToken);
        var succeeded = 0; var failed = 0; var candidateCount = 0;
        foreach (var instrument in universe.OrderBy(item => item.Code, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var series = await _store.BuildSeriesAsync(instrument.InstrumentId, request.EvaluationBarDate, request.AnalyzedAtUtc, request.Parameters.RequiredHistoryCount, cancellationToken);
                var indicators = _indicatorEngine.Calculate(series, request.Parameters);
                var indicatorId = await _store.SaveIndicatorAsync(scanRunId, snapshotId, series, indicators, cancellationToken);
                if (indicators.DataStatus != "Ok")
                {
                    await _store.SaveExclusionAsync(scanRunId, instrument.InstrumentId, indicators.DataStatus, indicators.HistoryAvailableCount, indicators.HistoryRequiredCount, cancellationToken);
                }
                else
                {
                    var candidates = new[] { _scoringEngine.Evaluate(indicators, "Long", request.Parameters), _scoringEngine.Evaluate(indicators, "Short", request.Parameters) };
                    await _store.SaveCandidatesAsync(indicatorId, instrument.InstrumentId, candidates, request.AnalyzedAtUtc, cancellationToken);
                    candidateCount += candidates.Count(candidate => candidate.Matched);
                }
                succeeded++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                failed++;
                await _store.SaveExclusionAsync(scanRunId, instrument.InstrumentId, "InvalidData", 0, request.Parameters.RequiredHistoryCount, CancellationToken.None);
            }
            progress?.Report(new TechnicalScanProgress(succeeded + failed, universe.Count, candidateCount, failed));
        }
        var status = failed == 0 ? "Succeeded" : succeeded == 0 ? "Failed" : "PartiallySucceeded";
        await _store.CompleteScanRunAsync(scanRunId, status, succeeded, failed, request.AnalyzedAtUtc, cancellationToken);
        return new TechnicalScanResult(scanRunId, status, universe.Count, succeeded, failed, candidateCount);
    }
}
