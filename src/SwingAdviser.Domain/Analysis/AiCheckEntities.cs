namespace SwingAdviser.Domain.Analysis;

public sealed class AiCheckAttempt
{
    public int AttemptId { get; set; }
    public int CandidateResultId { get; set; }
    public int? TriggeringDailyUpdateRunId { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateOnly EvaluationBarDate { get; set; }
    /// <summary>Canonical, secret-free input sent to the AI. It is retained so the input hash can be reproduced.</summary>
    public string NormalizedInputSnapshotJson { get; set; } = string.Empty;
    public string NormalizedInputSnapshotHash { get; set; } = string.Empty;
    public int TechnicalInputManifestId { get; set; }
    public string StrategySnapshotHash { get; set; } = string.Empty;
    public string PromptTemplateVersion { get; set; } = string.Empty;
    public string PromptTemplateHash { get; set; } = string.Empty;
    public string CliExecutablePath { get; set; } = string.Empty;
    public string? CliVersion { get; set; }
    public string? Model { get; set; }
    public int TimeoutSeconds { get; set; }
    public string? SanitizedArguments { get; set; }
    public int? ExitCode { get; set; }
    public string? ErrorKind { get; set; }
    public string? SanitizedStderr { get; set; }
    public string? RawResponseHash { get; set; }
    public string? StructuredResultSha256 { get; set; }
    public bool IsStale { get; set; }
    public CandidateResult CandidateResult { get; set; } = null!;
    public DailyUpdateRun? TriggeringDailyUpdateRun { get; set; }
    public AnalysisInputManifest TechnicalInputManifest { get; set; } = null!;
}

public sealed class AiCheckResult
{
    public int ResultId { get; set; }
    public int AttemptId { get; set; }
    public string SchemaVersion { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? Verdict { get; set; }
    public string? Confidence { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? TechnicalView { get; set; }
    public string? FundamentalView { get; set; }
    public DateTime CheckedAtUtc { get; set; }
    public AiCheckAttempt Attempt { get; set; } = null!;
}

public sealed class AiCheckEvidenceItem
{
    public int EvidenceItemId { get; set; }
    public int ResultId { get; set; }
    public string EvidenceKind { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string Text { get; set; } = string.Empty;
    public AiCheckResult Result { get; set; } = null!;
}

public sealed class AiCheckSource
{
    public int SourceId { get; set; }
    public int ResultId { get; set; }
    public int Ordinal { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime RetrievedAtUtc { get; set; }
    public AiCheckResult Result { get; set; } = null!;
}

public sealed class AiCheckEvidenceCitation
{
    public int EvidenceItemId { get; set; }
    public int SourceOrdinal { get; set; }
    public AiCheckEvidenceItem EvidenceItem { get; set; } = null!;
}
