using SwingAdviser.Domain.Analysis;

namespace SwingAdviser.Application.Analysis;

/// <summary>Application service for deterministic, fail-closed technical candidate scanning.</summary>
public sealed class AllInstrumentScanService
{
    private readonly ITechnicalScanStore _store;
    private readonly TechnicalIndicatorEngine _indicatorEngine;
    private readonly CandidateScoringEngine _scoringEngine;
    private readonly int _maxConcurrentInstrumentAnalysis;

    public AllInstrumentScanService(
        ITechnicalScanStore store,
        TechnicalIndicatorEngine? indicatorEngine = null,
        CandidateScoringEngine? scoringEngine = null,
        int maxConcurrentInstrumentAnalysis = 1)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _indicatorEngine = indicatorEngine ?? new TechnicalIndicatorEngine();
        _scoringEngine = scoringEngine ?? new CandidateScoringEngine();
        _maxConcurrentInstrumentAnalysis = Math.Clamp(maxConcurrentInstrumentAnalysis, 1, 8);
    }

    public async Task<TechnicalScanResult> RunAsync(TechnicalScanRequest request, IProgress<TechnicalScanProgress>? progress, CancellationToken cancellationToken)
    {
        request.Parameters.Validate();
        var universe = await _store.GetEligibleUniverseAsync(request.EvaluationBarDate, request.AnalyzedAtUtc, cancellationToken);
        var snapshotId = await _store.GetOrCreateStrategySnapshotAsync(request.Parameters, request.AnalyzedAtUtc, cancellationToken);
        var scanRunId = await _store.CreateScanRunAsync(request, universe.Count, cancellationToken);
        if (_maxConcurrentInstrumentAnalysis > 1)
        {
            return await RunBoundedConcurrentAsync(request, progress, cancellationToken, universe, snapshotId, scanRunId);
        }

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

    /// <summary>
    /// EF Core's DbContext and SQLite writes are intentionally serialized. The expensive per-instrument
    /// indicator calculations run concurrently after their immutable input series has been frozen.
    /// </summary>
    private async Task<TechnicalScanResult> RunBoundedConcurrentAsync(
        TechnicalScanRequest request,
        IProgress<TechnicalScanProgress>? progress,
        CancellationToken cancellationToken,
        IReadOnlyList<TechnicalScanInstrument> universe,
        int snapshotId,
        int scanRunId)
    {
        using var storeGate = new SemaphoreSlim(1, 1);
        var completed = 0;
        var succeeded = 0;
        var failed = 0;
        var candidateCount = 0;
        await Parallel.ForEachAsync(
            universe.OrderBy(item => item.Code, StringComparer.Ordinal),
            new ParallelOptions { MaxDegreeOfParallelism = _maxConcurrentInstrumentAnalysis, CancellationToken = cancellationToken },
            async (instrument, token) =>
            {
                try
                {
                    PointInTimeAnalysisSeries series;
                    await storeGate.WaitAsync(token);
                    try
                    {
                        series = await _store.BuildSeriesAsync(instrument.InstrumentId, request.EvaluationBarDate, request.AnalyzedAtUtc, request.Parameters.RequiredHistoryCount, token);
                    }
                    finally
                    {
                        storeGate.Release();
                    }

                    var indicators = _indicatorEngine.Calculate(series, request.Parameters);
                    IReadOnlyList<CandidateEvaluation>? candidates = indicators.DataStatus == "Ok"
                        ? [_scoringEngine.Evaluate(indicators, "Long", request.Parameters), _scoringEngine.Evaluate(indicators, "Short", request.Parameters)]
                        : null;

                    await storeGate.WaitAsync(token);
                    try
                    {
                        var indicatorId = await _store.SaveIndicatorAsync(scanRunId, snapshotId, series, indicators, token);
                        if (candidates is null)
                        {
                            await _store.SaveExclusionAsync(scanRunId, instrument.InstrumentId, indicators.DataStatus, indicators.HistoryAvailableCount, indicators.HistoryRequiredCount, token);
                        }
                        else
                        {
                            await _store.SaveCandidatesAsync(indicatorId, instrument.InstrumentId, candidates, request.AnalyzedAtUtc, token);
                            Interlocked.Add(ref candidateCount, candidates.Count(candidate => candidate.Matched));
                        }
                    }
                    finally
                    {
                        storeGate.Release();
                    }

                    Interlocked.Increment(ref succeeded);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    Interlocked.Increment(ref failed);
                    await storeGate.WaitAsync(CancellationToken.None);
                    try
                    {
                        await _store.SaveExclusionAsync(scanRunId, instrument.InstrumentId, "InvalidData", 0, request.Parameters.RequiredHistoryCount, CancellationToken.None);
                    }
                    finally
                    {
                        storeGate.Release();
                    }
                }
                finally
                {
                    var totalCompleted = Interlocked.Increment(ref completed);
                    progress?.Report(new TechnicalScanProgress(totalCompleted, universe.Count, Volatile.Read(ref candidateCount), Volatile.Read(ref failed)));
                }
            });
        var status = failed == 0 ? "Succeeded" : succeeded == 0 ? "Failed" : "PartiallySucceeded";
        await _store.CompleteScanRunAsync(scanRunId, status, succeeded, failed, request.AnalyzedAtUtc, cancellationToken);
        return new TechnicalScanResult(scanRunId, status, universe.Count, succeeded, failed, candidateCount);
    }
}
