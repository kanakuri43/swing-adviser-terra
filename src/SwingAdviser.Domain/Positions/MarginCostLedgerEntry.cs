namespace SwingAdviser.Domain.Positions;

/// <summary>Append-only margin-carrying-cost ledger entry; unknown values remain null rather than inferred as zero.</summary>
public sealed class MarginCostLedgerEntry
{
    public int LedgerEntryId { get; set; }
    public int MarginLotId { get; set; }
    public string CostType { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? Rate { get; set; }
    public string? RateUnit { get; set; }
    public string? DayCountConvention { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime AvailableAtUtc { get; set; }
    public DateTime? ObservedAtUtc { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public int Revision { get; set; }
    public int? SupersedesId { get; set; }

    public MarginLot MarginLot { get; set; } = null!;
    public MarginCostLedgerEntry? Supersedes { get; set; }
}
