using SwingAdviser.Domain.Analysis;

namespace SwingAdviser.Application.Analysis;

/// <summary>Application service for deterministic, fail-closed technical candidate scanning.</summary>
public sealed class AllInstrumentScanService
{
    private readonly ITechnicalScanStore _store;
    private readonly TechnicalIndicatorEngine _indicatorEngine;
    private readonly CandidateScoringEngine _scoringEngine;
    // At most about 16 * 201 manifest-bar rows are written at once on an initial scan.
    // This keeps SQLite transactions bounded while replacing per-instrument input queries.
    private const int InputReadBatchSize = 16;
    private const int PersistenceBatchSize = 64;

    public AllInstrumentScanService(
        ITechnicalScanStore store,
        TechnicalIndicatorEngine? indicatorEngine = null,
        CandidateScoringEngine? scoringEngine = null,
        int maxConcurrentInstrumentAnalysis = 1)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _indicatorEngine = indicatorEngine ?? new TechnicalIndicatorEngine();
        _scoringEngine = scoringEngine ?? new CandidateScoringEngine();
        // Persistence is intentionally batched through the one DbContext.  Keeping this
        // argument maintains the runtime configuration contract; concurrent calculation is
        // not useful while input freezing and SQLite writes share that context.
        _ = Math.Clamp(maxConcurrentInstrumentAnalysis, 1, 8);
    }

    public async Task<TechnicalScanResult> RunAsync(TechnicalScanRequest request, IProgress<TechnicalScanProgress>? progress, CancellationToken cancellationToken)
    {
        request.Parameters.Validate();
        var universe = await _store.GetEligibleUniverseAsync(request.EvaluationBarDate, request.AnalyzedAtUtc, cancellationToken);
        var snapshotId = await _store.GetOrCreateStrategySnapshotAsync(request.Parameters, request.AnalyzedAtUtc, cancellationToken);
        var scanRunId = await _store.CreateScanRunAsync(request, universe.Count, cancellationToken);
        var succeeded = 0;
        var failed = 0;
        var candidateCount = 0;
        var pending = new List<TechnicalScanPersistenceItem>(PersistenceBatchSize);

        async Task FlushPendingAsync(CancellationToken token)
        {
            if (pending.Count == 0) return;
            var batch = pending.ToArray();
            pending.Clear();
            await _store.SaveBatchAsync(scanRunId, snapshotId, batch, request.AnalyzedAtUtc, token);
        }

        foreach (var instrumentBatch in universe.OrderBy(item => item.Code, StringComparer.Ordinal).Chunk(InputReadBatchSize))
        {
            IReadOnlyList<PointInTimeAnalysisSeries> seriesBatch;
            try
            {
                seriesBatch = await _store.BuildSeriesBatchAsync(instrumentBatch.Select(item => item.InstrumentId).ToArray(), request.EvaluationBarDate, request.AnalyzedAtUtc, request.Parameters.RequiredHistoryCount, cancellationToken);
                if (seriesBatch.Count != instrumentBatch.Length || seriesBatch.Select(series => series.InstrumentId).Order().SequenceEqual(instrumentBatch.Select(item => item.InstrumentId).Order()) is false)
                {
                    throw new InvalidOperationException("The input batch did not return exactly one series per requested instrument.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                foreach (var instrument in instrumentBatch)
                {
                    failed++;
                    pending.Add(new FailedTechnicalScanPersistenceItem(instrument.InstrumentId, "InvalidData", 0, request.Parameters.RequiredHistoryCount));
                    progress?.Report(new TechnicalScanProgress(succeeded + failed, universe.Count, candidateCount, failed));
                }
                if (pending.Count >= PersistenceBatchSize) await FlushPendingAsync(cancellationToken);
                continue;
            }

            foreach (var series in seriesBatch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var reusable = await _store.FindReusableResultAsync(series.ManifestHash, snapshotId, cancellationToken);
                    if (reusable is not null)
                    {
                        pending.Add(new ReusedTechnicalScanPersistenceItem(series.InstrumentId, reusable));
                        candidateCount += reusable.MatchedCandidateCount;
                    }
                    else
                    {
                        var indicators = _indicatorEngine.Calculate(series, request.Parameters);
                        var candidates = indicators.DataStatus == "Ok"
                            ? new[] { _scoringEngine.Evaluate(indicators, "Long", request.Parameters), _scoringEngine.Evaluate(indicators, "Short", request.Parameters) }
                            : Array.Empty<CandidateEvaluation>();
                        pending.Add(new ComputedTechnicalScanPersistenceItem(series, indicators, candidates));
                        candidateCount += candidates.Count(candidate => candidate.Matched);
                    }
                    succeeded++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch
                {
                    failed++;
                    pending.Add(new FailedTechnicalScanPersistenceItem(series.InstrumentId, "InvalidData", 0, request.Parameters.RequiredHistoryCount));
                }
                if (pending.Count >= PersistenceBatchSize) await FlushPendingAsync(cancellationToken);
                progress?.Report(new TechnicalScanProgress(succeeded + failed, universe.Count, candidateCount, failed));
            }
        }
        await FlushPendingAsync(cancellationToken);
        var status = failed == 0 ? "Succeeded" : succeeded == 0 ? "Failed" : "PartiallySucceeded";
        await _store.CompleteScanRunAsync(scanRunId, status, succeeded, failed, request.AnalyzedAtUtc, cancellationToken);
        return new TechnicalScanResult(scanRunId, status, universe.Count, succeeded, failed, candidateCount);
    }
}
