namespace SwingAdviser.Domain.MarketData;

/// <summary>Stable internal identity for a listed instrument.</summary>
public sealed class Instrument
{
    public int InstrumentId { get; set; }

    public DateTime FirstObservedAtUtc { get; set; }

    public ICollection<InstrumentMasterRevision> MasterRevisions { get; } = new List<InstrumentMasterRevision>();

    public ICollection<MarginRegulationRevision> MarginRegulationRevisions { get; } = new List<MarginRegulationRevision>();

    public ICollection<DailyBar> DailyBars { get; } = new List<DailyBar>();

    public ICollection<CorporateAction> CorporateActions { get; } = new List<CorporateAction>();

    public ICollection<FundamentalDataSnapshot> FundamentalDataSnapshots { get; } = new List<FundamentalDataSnapshot>();
}
