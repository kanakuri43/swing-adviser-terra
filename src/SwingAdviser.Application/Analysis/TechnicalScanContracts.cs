using SwingAdviser.Domain.Analysis;

namespace SwingAdviser.Application.Analysis;

public sealed record TechnicalScanRequest(DateOnly EvaluationBarDate, DateTime AnalyzedAtUtc, int? DailyUpdateRunId, string UniverseDefinitionHash, TechnicalStrategyParameters Parameters);
public sealed record TechnicalScanInstrument(int InstrumentId, string Code);
public sealed record TechnicalScanProgress(int Completed, int Total, int CandidateCount, int FailedCount);
public sealed record TechnicalScanResult(int ScanRunId, string Status, int TotalInstruments, int SucceededCount, int FailedCount, int CandidateCount);

public interface ITechnicalScanStore
{
    Task<IReadOnlyList<TechnicalScanInstrument>> GetEligibleUniverseAsync(DateOnly evaluationBarDate, DateTime analyzedAtUtc, CancellationToken cancellationToken);
    Task<int> GetOrCreateStrategySnapshotAsync(TechnicalStrategyParameters parameters, DateTime createdAtUtc, CancellationToken cancellationToken);
    Task<int> CreateScanRunAsync(TechnicalScanRequest request, int totalInstruments, CancellationToken cancellationToken);
    Task<PointInTimeAnalysisSeries> BuildSeriesAsync(int instrumentId, DateOnly evaluationBarDate, DateTime analyzedAtUtc, int requiredHistoryCount, CancellationToken cancellationToken);
    Task<int> SaveIndicatorAsync(int scanRunId, int strategySnapshotId, PointInTimeAnalysisSeries series, IndicatorComputation indicator, CancellationToken cancellationToken);
    Task SaveCandidatesAsync(int indicatorResultId, int instrumentId, IReadOnlyList<CandidateEvaluation> candidates, DateTime createdAtUtc, CancellationToken cancellationToken);
    Task SaveExclusionAsync(int scanRunId, int instrumentId, string reason, int available, int required, CancellationToken cancellationToken);
    Task CompleteScanRunAsync(int scanRunId, string status, int succeededCount, int failedCount, DateTime completedAtUtc, CancellationToken cancellationToken);
}
