namespace SwingAdviser.Application.Positions;

/// <summary>Read model for the manual registration UI; it deliberately contains no pricing or automatic trade actions.</summary>
public sealed record ManualPositionOverview(
    int PositionId,
    string Code,
    string Name,
    string Side,
    decimal CurrentQuantity,
    string Strategy,
    string ReconciliationStatus,
    string? EvaluationDecision = null,
    DateOnly? EvaluationBarDate = null,
    string? EvaluationOutcome = null,
    decimal? StopCandidate = null,
    decimal? TakeProfitCandidate = null,
    DateOnly? EarliestRepaymentDate = null,
    decimal? ConfirmedCost = null,
    decimal? ReferenceNetProfitAndLoss = null,
    DateOnly? LatestBarDate = null,
    decimal? LatestClose = null);

public sealed record ManualExecutionOverview(
    int TradeExecutionId,
    string Code,
    string Name,
    string Side,
    string ExecutionRole,
    string RegistrationSource,
    DateTimeOffset ExecutedAt,
    decimal Price,
    int Quantity,
    int Revision,
    string Status,
    string? Notes);

public interface IManualPositionOverviewReader
{
    Task<IReadOnlyList<ManualPositionOverview>> GetOpenPositionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManualExecutionOverview>> GetExecutionsAsync(CancellationToken cancellationToken = default);
}
