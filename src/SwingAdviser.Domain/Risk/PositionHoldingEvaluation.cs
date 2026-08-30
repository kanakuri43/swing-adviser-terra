using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Domain.Risk;

/// <summary>Immutable fail-closed aggregation of the lot-level holding evaluations for a position.</summary>
public sealed class PositionHoldingEvaluation
{
    public int PositionEvaluationId { get; set; }
    public int PositionId { get; set; }
    public DateOnly EvaluationBarDate { get; set; }
    public DateTime EvaluatedAtUtc { get; set; }
    public string? AggregatedDecision { get; set; }
    public string PartialExitStatus { get; set; } = string.Empty;
    public decimal? PartialExitTotalCandidateQuantity { get; set; }
    public string EvaluationOutcome { get; set; } = string.Empty;
    public string LotEvaluationsJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public Position Position { get; set; } = null!;
}
