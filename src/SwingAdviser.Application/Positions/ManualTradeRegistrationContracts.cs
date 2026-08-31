using SwingAdviser.Domain.Positions;

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

/// <summary>Repository boundary for the manual trade workflow. Implementations must persist one SaveChanges call atomically.</summary>
public interface IManualTradeRegistrationStore
{
    Task<Position?> FindPositionAsync(int positionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MarginLot>> GetOpenLotsAsync(int positionId, CancellationToken cancellationToken);

    void AddOpening(Position position, TradeExecution execution, MarginLot lot);

    void AddClose(TradeExecution execution, IReadOnlyCollection<TradeExecutionLotAllocation> allocations);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
