namespace SwingAdviser.Domain.MarketData;

/// <summary>Append-only revision of an instrument-master record.</summary>
public sealed class InstrumentMasterRevision
{
    public int InstrumentMasterRevisionId { get; set; }
    public int InstrumentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MarketSegment { get; set; } = string.Empty;
    public string InstrumentType { get; set; } = string.Empty;
    public string ListedStatus { get; set; } = string.Empty;
    public string ScanEligibility { get; set; } = string.Empty;
    public DateOnly EffectiveAtDate { get; set; }
    public DateTime AvailableAtUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SourceFileHash { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
    public int Revision { get; set; }
    public int? SupersedesRevisionId { get; set; }
    public string Status { get; set; } = string.Empty;

    public Instrument Instrument { get; set; } = null!;
    public InstrumentMasterRevision? SupersedesRevision { get; set; }
}
