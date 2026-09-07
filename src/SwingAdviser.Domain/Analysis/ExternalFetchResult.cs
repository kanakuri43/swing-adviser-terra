using SwingAdviser.Domain.MarketData;

namespace SwingAdviser.Domain.Analysis;

/// <summary>Success or failure of one external-data fetch attempt.</summary>
public sealed class ExternalFetchResult
{
    public int FetchResultId { get; set; }
    public int? DailyUpdateRunId { get; set; }
    public int? DailyUpdateFetchCheckpointId { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public int? InstrumentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorKind { get; set; }
    public string? ErrorMessage { get; set; }
    public int? RecordCount { get; set; }
    public DateTime AttemptedAtUtc { get; set; }

    public DailyUpdateRun? DailyUpdateRun { get; set; }
    public DailyUpdateFetchCheckpoint? DailyUpdateFetchCheckpoint { get; set; }
    public Instrument? Instrument { get; set; }
}
