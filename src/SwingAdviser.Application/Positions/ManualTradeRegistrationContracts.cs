using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Risk;

namespace SwingAdviser.Application.Positions;

/// <summary>Explicit user input for a new margin opening. Candidate data may only prefill identifying fields.</summary>
public sealed record ManualOpenTradeRequest(
    int InstrumentId,
    string Side,
    DateTimeOffset ExecutedAt,
    decimal Price,
    int Quantity,
    string Currency,
    string AppliedStrategyKey,
    string AppliedStrategyVersion,
    int? ExistingPositionId = null,
    int? CandidateResultId = null,
    string? Notes = null,
    string? Memo = null,
    bool UserConfirmed = false);

/// <summary>A user-confirmed allocation. A close cannot be saved without allocations covering it exactly.</summary>
public sealed record ManualLotAllocationInput(int MarginLotId, decimal Quantity);

/// <summary>Explicit user input for a close execution. No allocation order (including FIFO) is inferred.</summary>
public sealed record ManualCloseTradeRequest(
    int PositionId,
    DateTimeOffset ExecutedAt,
    decimal Price,
    int Quantity,
    string Currency,
    IReadOnlyCollection<ManualLotAllocationInput> Allocations,
    string? Notes = null,
    bool UserConfirmed = false);

public sealed record ManualTradeRegistrationResult(int PositionId, int TradeExecutionId, IReadOnlyList<int> MarginLotIds);

/// <summary>Immutable ATR provenance for a candidate-originated opening risk plan.</summary>
public sealed record CandidateRiskPlanSource(
    int CandidateResultId,
    int InstrumentId,
    string Direction,
    string SignalPurpose,
    bool Matched,
    int IndicatorResultId,
    int IndicatorInstrumentId,
    string IndicatorDataStatus,
    decimal? Atr14,
    DateOnly EvaluationBarDate,
    DateTime AnalyzedAtUtc,
    int StrategyParameterSnapshotId,
    string CorporateActionSetHash);

/// <summary>Repository boundary for the manual trade workflow. Implementations must persist one SaveChanges call atomically.</summary>
public interface IManualTradeRegistrationStore
{
    Task<Position?> FindPositionAsync(int positionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MarginLot>> GetOpenLotsAsync(int positionId, CancellationToken cancellationToken);

    Task<CandidateRiskPlanSource?> GetCandidateRiskPlanSourceAsync(int candidateResultId, CancellationToken cancellationToken);

    void AddOpening(Position position, TradeExecution execution, MarginLot lot, RiskBasisSnapshot? riskBasis = null, RiskPlan? riskPlan = null);

    void AddClose(TradeExecution execution, IReadOnlyCollection<TradeExecutionLotAllocation> allocations);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
