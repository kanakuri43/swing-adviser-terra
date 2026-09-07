using Microsoft.EntityFrameworkCore;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Domain.Risk;

namespace SwingAdviser.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the swing-adviser SQLite database.
/// Business schema is added incrementally through EF Core migrations.
/// </summary>
public sealed class SwingAdviserDbContext : DbContext
{
    public SwingAdviserDbContext(DbContextOptions<SwingAdviserDbContext> options)
        : base(options)
    {
    }

    public DbSet<Instrument> Instruments => Set<Instrument>();

    public DbSet<InstrumentMasterRevision> InstrumentMasterRevisions => Set<InstrumentMasterRevision>();

    public DbSet<MarginRegulationRevision> MarginRegulationRevisions => Set<MarginRegulationRevision>();

    public DbSet<DailyBar> DailyBars => Set<DailyBar>();

    public DbSet<DailyBarHistoryCoverage> DailyBarHistoryCoverages => Set<DailyBarHistoryCoverage>();

    public DbSet<CorporateAction> CorporateActions => Set<CorporateAction>();

    public DbSet<FundamentalDataSnapshot> FundamentalDataSnapshots => Set<FundamentalDataSnapshot>();

    public DbSet<AnalysisInputManifest> AnalysisInputManifests => Set<AnalysisInputManifest>();

    public DbSet<AnalysisInputManifestBar> AnalysisInputManifestBars => Set<AnalysisInputManifestBar>();

    public DbSet<AnalysisInputManifestCorporateAction> AnalysisInputManifestCorporateActions => Set<AnalysisInputManifestCorporateAction>();

    public DbSet<StrategyParameterSnapshot> StrategyParameterSnapshots => Set<StrategyParameterSnapshot>();

    public DbSet<DailyUpdateRun> DailyUpdateRuns => Set<DailyUpdateRun>();

    public DbSet<ExternalFetchResult> ExternalFetchResults => Set<ExternalFetchResult>();

    public DbSet<DailyUpdateFetchCheckpoint> DailyUpdateFetchCheckpoints => Set<DailyUpdateFetchCheckpoint>();

    public DbSet<ScanRun> ScanRuns => Set<ScanRun>();

    public DbSet<IndicatorResult> IndicatorResults => Set<IndicatorResult>();

    public DbSet<ScanExclusion> ScanExclusions => Set<ScanExclusion>();

    public DbSet<CandidateResult> CandidateResults => Set<CandidateResult>();

    public DbSet<Position> Positions => Set<Position>();

    public DbSet<TradeExecution> TradeExecutions => Set<TradeExecution>();

    public DbSet<MarginLot> MarginLots => Set<MarginLot>();

    public DbSet<MarginLotContractTermRevision> MarginLotContractTermRevisions => Set<MarginLotContractTermRevision>();

    public DbSet<TradeExecutionLotAllocation> TradeExecutionLotAllocations => Set<TradeExecutionLotAllocation>();

    public DbSet<PositionCorporateActionAdjustment> PositionCorporateActionAdjustments => Set<PositionCorporateActionAdjustment>();

    public DbSet<MarginCostLedgerEntry> MarginCostLedgerEntries => Set<MarginCostLedgerEntry>();

    public DbSet<RiskBasisSnapshot> RiskBasisSnapshots => Set<RiskBasisSnapshot>();

    public DbSet<RiskPlan> RiskPlans => Set<RiskPlan>();

    public DbSet<LotHoldingEvaluation> LotHoldingEvaluations => Set<LotHoldingEvaluation>();

    public DbSet<PositionHoldingEvaluation> PositionHoldingEvaluations => Set<PositionHoldingEvaluation>();

    public DbSet<AiCheckAttempt> AiCheckAttempts => Set<AiCheckAttempt>();
    public DbSet<AiCheckResult> AiCheckResults => Set<AiCheckResult>();
    public DbSet<AiCheckEvidenceItem> AiCheckEvidenceItems => Set<AiCheckEvidenceItem>();
    public DbSet<AiCheckSource> AiCheckSources => Set<AiCheckSource>();
    public DbSet<AiCheckEvidenceCitation> AiCheckEvidenceCitations => Set<AiCheckEvidenceCitation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SwingAdviserDbContext).Assembly);
    }
}
