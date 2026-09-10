using System.Security.Cryptography;
using System.Text;
using SwingAdviser.Application.Analysis;
using SwingAdviser.Application.DailyUpdates;
using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Infrastructure.Analysis;
using SwingAdviser.Infrastructure.MarketData;
using SwingAdviser.Infrastructure.Persistence;
using SwingAdviser.Infrastructure.Positions;

namespace SwingAdviser.Infrastructure.DailyUpdates;

/// <summary>Desktop composition root for a user-triggered, non-trading daily analysis update.</summary>
public sealed class RuntimeDailyUpdateServices : IDisposable
{
    private const string DefaultJpxListedIssuesUrl = "https://www.jpx.co.jp/markets/statistics-equities/misc/tvdivq0000001vg2-att/data_j.xlsx";
    private const string DefaultJpxMarginIssuesUrl = "https://www.jpx.co.jp/listing/others/margin/index.html";
    private readonly SwingAdviserDbContext _context;
    private readonly HttpClient _httpClient;

    private RuntimeDailyUpdateServices(SwingAdviserDbContext context, HttpClient httpClient, IDailyUpdateExecutionService runner, IDailyUpdateOverviewReader overviewReader)
    {
        _context = context;
        _httpClient = httpClient;
        Runner = runner;
        OverviewReader = overviewReader;
    }

    public IDailyUpdateExecutionService Runner { get; }
    public IDailyUpdateOverviewReader OverviewReader { get; }

    public static RuntimeDailyUpdateServices Create(AiCheckQueueService aiQueue)
    {
        ArgumentNullException.ThrowIfNull(aiQueue);
        var context = RuntimeSwingAdviserDbContextFactory.CreateMigratedContext();
        var timeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("SWING_ADVISER_DATA_TIMEOUT_SECONDS"), out var configuredTimeout) ? configuredTimeout : 60;
        var maxConcurrentInstrumentFetches = BoundedEnvironmentValue("SWING_ADVISER_DATA_MAX_CONCURRENCY", 8, 1, 8);
        var maxConcurrentInstrumentAnalysis = BoundedEnvironmentValue("SWING_ADVISER_SCAN_MAX_CONCURRENCY", 4, 1, 8);
        var historyLookbackYears = BoundedEnvironmentValue("SWING_ADVISER_HISTORY_LOOKBACK_YEARS", 5, 1, 10);
        var checkpointValidityMinutes = BoundedEnvironmentValue("SWING_ADVISER_FETCH_CHECKPOINT_VALIDITY_MINUTES", 360, 1, 1440);
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SwingAdviser/1.0 (decision-support; no-ordering)");
        var repository = new MarketDataRepository(context);
        var ingestion = new MarketDataIngestionService(repository,
            CreateListedIssuesSource(httpClient), CreateMarginIssuesSource(httpClient),
            new YahooFinanceClient(httpClient, ConfiguredUri("SWING_ADVISER_YAHOO_BASE_URL", "https://query1.finance.yahoo.com/")),
            maxConcurrentInstrumentFetches: maxConcurrentInstrumentFetches);
        var technicalState = new DailyUpdateTechnicalState();
        var strategyParameters = new TechnicalStrategyParameters();
        var scan = new AllInstrumentScanService(new EfTechnicalScanStore(context, historyLookbackYears), maxConcurrentInstrumentAnalysis: maxConcurrentInstrumentAnalysis);
        var stages = new IDailyUpdateStage[]
        {
            new MarketDataDailyUpdateStage(ingestion, context, historyLookbackYears, strategyParameters.RequiredHistoryCount, TimeSpan.FromMinutes(checkpointValidityMinutes)),
            new DataAvailabilityVerificationDailyUpdateStage(context),
            new CorporateActionAdjustmentDailyUpdateStage(new CorporateActionPositionAdjustmentService(new EfCorporateActionPositionAdjustmentStore(context))),
            new PointInTimePreparationDailyUpdateStage(),
            new TechnicalAnalysisDailyUpdateStage(scan, technicalState, strategyParameters),
            new CandidateExtractionDailyUpdateStage("Long", technicalState, new EfCandidateResultCounter(context)),
            new CandidateExtractionDailyUpdateStage("Short", technicalState, new EfCandidateResultCounter(context)),
            new HoldingReevaluationDailyUpdateStage(new HoldingReevaluationService(new EfHoldingReevaluationStore(context))),
            new PersistResultsDailyUpdateStage(),
            new PublishResultsDailyUpdateStage(new DurableResultsPublisher()),
            new AiQueueDailyUpdateStage(aiQueue),
        };
        var orchestrator = new DailyUpdateOrchestrator(new EfDailyUpdateRunStore(context), stages);
        return new RuntimeDailyUpdateServices(context, httpClient, new RuntimeDailyUpdateExecutionService(orchestrator), new EfDailyUpdateOverviewReader(context));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _context.Dispose();
    }

    private static IJpxListedIssuesSource CreateListedIssuesSource(HttpClient client) =>
        new JpxListedIssuesClient(client, ConfiguredUri("SWING_ADVISER_JPX_LISTED_ISSUES_URL", DefaultJpxListedIssuesUrl));

    private static IJpxMarginIssuesSource CreateMarginIssuesSource(HttpClient client) =>
        new JpxMarginIssuesClient(client, ConfiguredUri("SWING_ADVISER_JPX_MARGIN_ISSUES_URL", DefaultJpxMarginIssuesUrl));

    private static Uri ConfiguredUri(string name, string fallback) => ConfiguredOptionalUri(name) ?? new Uri(fallback);
    private static Uri? ConfiguredOptionalUri(string name) => Uri.TryCreate(Environment.GetEnvironmentVariable(name), UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri : null;
    private static int BoundedEnvironmentValue(string name, int fallback, int minimum, int maximum)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var configured) ? Math.Clamp(configured, minimum, maximum) : fallback;

    private sealed class DurableResultsPublisher : IDailyUpdateResultsPublisher
    {
        public Task PublishAsync(DailyUpdateRunResultPlaceholder update, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RuntimeDailyUpdateExecutionService(DailyUpdateOrchestrator orchestrator) : IDailyUpdateExecutionService
    {
        private const string UniverseDefinition = "market=JPX;segments=Prime,Standard,Growth;type=DomesticCommonStock;listed=Listed;scan=Eligible";

        public Task<DailyUpdateRunResult> RunAsync(IProgress<DailyUpdateStepProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var requestedAt = DateTime.UtcNow;
            var request = new DailyUpdateRequest(DailyUpdateEvaluationDateResolver.Resolve(requestedAt), requestedAt, Hash(UniverseDefinition));
            return orchestrator.RunAsync(request, progress, cancellationToken);
        }

        private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
