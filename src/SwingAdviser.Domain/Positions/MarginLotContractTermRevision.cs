namespace SwingAdviser.Domain.Positions;

/// <summary>Append-only broker-confirmed contract term revision for a margin lot.</summary>
public sealed class MarginLotContractTermRevision
{
    public int ContractTermRevisionId { get; set; }
    public int MarginLotId { get; set; }
    public string MarginCategory { get; set; } = string.Empty;
    public string Broker { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string TermType { get; set; } = string.Empty;
    public DateOnly? FinalRepaymentDate { get; set; }
    public DateTime ConfirmedAtUtc { get; set; }
    public string? Evidence { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public int Revision { get; set; }
    public int? SupersedesRevisionId { get; set; }
    public string Status { get; set; } = string.Empty;

    public MarginLot MarginLot { get; set; } = null!;
    public MarginLotContractTermRevision? SupersedesRevision { get; set; }
}
