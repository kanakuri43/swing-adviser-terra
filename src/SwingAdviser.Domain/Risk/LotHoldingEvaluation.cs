using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Domain.Risk;

/// <summary>Immutable holding-risk evaluation for one lot; it never changes executions or quantities.</summary>
public sealed class LotHoldingEvaluation
{
    public int LotEvaluationId { get; set; }
    public int MarginLotId { get; set; }
    public int PositionId { get; set; }
    public DateOnly EvaluationBarDate { get; set; }
    public DateTime EvaluatedAtUtc { get; set; }
    public int DailyBarId { get; set; }
    public int RiskPlanRevisionId { get; set; }
    public string? Decision { get; set; }
    public bool StopReachedToday { get; set; }
    public bool TargetReachedToday { get; set; }
    public string PriorTargetReachState { get; set; } = string.Empty;
    public DateOnly? PriorTargetFirstReachBarDate { get; set; }
    public string TechnicalReversalState { get; set; } = string.Empty;
    public string MacdReversalState { get; set; } = string.Empty;
    public string Ema20ReversalState { get; set; } = string.Empty;
    public string PartialExitStatus { get; set; } = string.Empty;
    public decimal? PartialExitCandidateQuantity { get; set; }
    public string EvaluationOutcome { get; set; } = string.Empty;
    public string EvaluationEvidenceJson { get; set; } = string.Empty;
    public string? DiagnosticsJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public MarginLot MarginLot { get; set; } = null!;
    public Position Position { get; set; } = null!;
    public DailyBar DailyBar { get; set; } = null!;
    public RiskPlan RiskPlanRevision { get; set; } = null!;
}
