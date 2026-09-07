using System.Net;
using System.Net.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.MarketData;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Tests;

public class MarketDataIngestionTests
{
    [Fact]
    public void JpxListedIssuesParser_ParsesQuotedCsvAndKeepsOnlyDomesticCommonStocksEligible()
    {
        const string csv = "コード,銘柄名,市場・商品区分\n7203,トヨタ自動車,プライム（内国株式）\n1306,\"ＮＥＸＴ,ＦＵＮＤＳ\",ETF\n";

        var records = JpxListedIssuesParser.Parse(csv);

        Assert.Collection(records,
            record =>
            {
                Assert.Equal("7203", record.Code);
                Assert.Equal("Prime", record.MarketSegment);
                Assert.Equal("DomesticCommonStock", record.InstrumentType);
                Assert.Equal("Eligible", record.ScanEligibility);
            },
            record => Assert.Equal("Ineligible", record.ScanEligibility));
    }

    [Fact]
    public void JpxMarginIssuesParser_LeavesAbsenceUnknownAndCombinesSystemAndLoanableRows()
    {
        const string html = """
            <h2>制度信用選定銘柄</h2><table><tr><th>選定日</th><th>銘柄名</th><th>コード</th></tr><tr><td>2026/08/01</td><td>銘柄A</td><td>7203</td></tr></table>
            <h2>貸借選定銘柄</h2><table><tr><th>選定日</th><th>銘柄名</th><th>コード</th></tr><tr><td>2026/08/01</td><td>銘柄A</td><td>7203</td></tr><tr><td>2026/08/01</td><td>銘柄B</td><td>6758</td></tr></table>
            """;

        var records = JpxMarginIssuesParser.Parse(html).ToDictionary(record => record.Code);

        Assert.Equal("Eligible", records["7203"].SystemMarginEligible);
        Assert.Equal("Eligible", records["7203"].ShortSellEligible);
        Assert.Equal("Unknown", records["6758"].SystemMarginEligible);
        Assert.Equal("Eligible", records["6758"].ShortSellEligible);
    }

    [Fact]
    public void YahooFinanceParser_RejectsInvalidOhlcvAndMarksProviderEventsWithoutAnnouncementTime()
    {
        const string json = """
            {"chart":{"result":[{"timestamp":[1787896800,1787983200],"indicators":{"quote":[{"open":[100,null],"high":[110,100],"low":[90,101],"close":[105,99],"volume":[1000,5]}],"adjclose":[{"adjclose":[101,99]}]},"events":{"splits":{"1787896800":{"date":1787896800,"numerator":2,"denominator":1}},"dividends":{"1787896800":{"date":1787896800,"amount":10}}}}],"error":null}}
            """;

        var snapshot = YahooFinanceParser.ParseChart(json);

        var bar = Assert.Single(snapshot.DailyBars);
        Assert.Equal(new DateOnly(2026, 8, 28), bar.TradingDate);
        Assert.Equal(101m, bar.AdjClose);
        Assert.Equal(2, snapshot.CorporateActions.Count);
        Assert.All(snapshot.CorporateActions, action => Assert.StartsWith("YahooFinanceChartApiV8:", action.SourceEventId));
    }

    [Fact]
    public async Task Repository_AppendsOnlyCorrectedBarsAndActions_AndDoesNotAppendIdenticalPayload()
    {
        using var connection = OpenMigratedConnection();
        await using var context = CreateContext(connection);
        var observedAt = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var instrument = new Instrument { FirstObservedAtUtc = observedAt };
        context.Instruments.Add(instrument);
        await context.SaveChangesAsync();
        var repository = new MarketDataRepository(context);
        var original = new YahooChartSourceSnapshot(
            new[] { new YahooDailyBarSourceRecord(new DateOnly(2026, 8, 28), 100m, 110m, 90m, 105m, 1000, 101m) },
            new[] { new CorporateActionSourceRecord("CashDividend", new DateOnly(2026, 9, 30), observedAt, null, null, 10m, "JPY", "YahooFinanceChartApiV8:dividend:1") });

        Assert.Equal(2, await repository.ImportYahooChartAsync(instrument.InstrumentId, original, observedAt, CancellationToken.None));
        Assert.Equal(0, await repository.ImportYahooChartAsync(instrument.InstrumentId, original, observedAt.AddMinutes(1), CancellationToken.None));

        var corrected = original with
        {
            DailyBars = new[] { new YahooDailyBarSourceRecord(new DateOnly(2026, 8, 28), 100m, 110m, 90m, 106m, 1000, 101m) },
            CorporateActions = new[] { new CorporateActionSourceRecord("CashDividend", new DateOnly(2026, 9, 30), observedAt, null, null, 11m, "JPY", "YahooFinanceChartApiV8:dividend:1") },
        };
        Assert.Equal(2, await repository.ImportYahooChartAsync(instrument.InstrumentId, corrected, observedAt.AddMinutes(2), CancellationToken.None));
        context.ChangeTracker.Clear();

        var bars = await context.DailyBars.OrderBy(bar => bar.Revision).ToListAsync();
        Assert.Equal(new[] { 1, 2 }, bars.Select(bar => bar.Revision));
        Assert.Equal(bars[0].DailyBarId, bars[1].SupersedesId);
        var actions = await context.CorporateActions.OrderBy(action => action.Revision).ToListAsync();
        Assert.Equal(actions[0].CorporateActionId, actions[1].SupersedesId);
        Assert.All(actions, action => Assert.Null(action.AnnouncedAtUtc));
        Assert.Equal(observedAt.AddMinutes(2), actions[1].AvailableAtUtc);
        Assert.Equal(observedAt, actions[1].FirstObservedAtUtc);
    }

    [Fact]
    public async Task Repository_RetainsFullHistoryConfirmationWhenImportingAnIncrementalCorrectionWindow()
    {
        using var connection = OpenMigratedConnection();
        await using var context = CreateContext(connection);
        var observedAt = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var instrument = new Instrument { FirstObservedAtUtc = observedAt };
        context.Instruments.Add(instrument);
        await context.SaveChangesAsync();
        var repository = new MarketDataRepository(context);

        await repository.ImportYahooChartAsync(instrument.InstrumentId,
            new YahooChartSourceSnapshot([new YahooDailyBarSourceRecord(new DateOnly(2000, 1, 4), 100m, 110m, 90m, 105m, 1000, null)], [], FullHistoryConfirmed: true),
            observedAt, CancellationToken.None);
        await repository.ImportYahooChartAsync(instrument.InstrumentId,
            new YahooChartSourceSnapshot([new YahooDailyBarSourceRecord(new DateOnly(2026, 8, 31), 200m, 210m, 190m, 205m, 2000, null)], [], FullHistoryConfirmed: false),
            observedAt.AddDays(1), CancellationToken.None);

        var coverage = await context.DailyBarHistoryCoverages.OrderByDescending(entity => entity.Revision).FirstAsync();
        Assert.True(coverage.FullHistoryConfirmed);
        Assert.Equal("Complete", coverage.Status);
        Assert.Equal(new DateOnly(2000, 1, 4), coverage.EarliestReturnedDate);
        Assert.Equal(new DateOnly(2026, 8, 31), coverage.LatestReturnedDate);
    }

    [Fact]
    public async Task RefreshPlanning_UsesEvaluationDateCacheAndFetchesOnlyMissingChartsFromFiniteWindow()
    {
        using var connection = OpenMigratedConnection();
        await using var context = CreateContext(connection);
        var requestedAt = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var covered = new Instrument { FirstObservedAtUtc = requestedAt };
        var missing = new Instrument { FirstObservedAtUtc = requestedAt };
        context.AddRange(covered, missing);
        await context.SaveChangesAsync();
        context.DailyBars.Add(new DailyBar
        {
            InstrumentId = covered.InstrumentId, TradingDate = new DateOnly(2026, 8, 28), Open = 100m, High = 101m, Low = 99m, Close = 100m, Volume = 1000,
            Source = "YahooFinanceChartApiV8", FetchedAtUtc = requestedAt, Revision = 1, Status = "Final",
        });
        context.DailyBars.Add(new DailyBar
        {
            InstrumentId = covered.InstrumentId, TradingDate = new DateOnly(2021, 8, 30), Open = 100m, High = 101m, Low = 99m, Close = 100m, Volume = 1000,
            Source = "YahooFinanceChartApiV8", FetchedAtUtc = requestedAt, Revision = 1, Status = "Final",
        });
        await context.SaveChangesAsync();

        var windowStartUtc = new DateTime(2021, 8, 28, 0, 0, 0, DateTimeKind.Utc);

        var targets = await new MarketDataRepository(context).CreateRefreshTargetsAsync(
            [(covered.InstrumentId, "7203"), (missing.InstrumentId, "6758")], new DateOnly(2026, 8, 28), requestedAt, windowStartUtc, CancellationToken.None);

        Assert.False(targets[0].RefreshChart);
        Assert.Equal(windowStartUtc, targets[0].ChartPeriodStartUtc);
        Assert.False(targets[0].RefreshFundamentals);
        Assert.True(targets[1].RefreshChart);
        Assert.Equal(windowStartUtc, targets[1].ChartPeriodStartUtc);
        Assert.False(targets[1].RefreshFundamentals);
    }

    [Fact]
    public async Task RefreshCheckpoint_ReusesOnlyMatchingValidSuccess_AndDetectsExpiryOrRevisions()
    {
        using var connection = OpenMigratedConnection();
        await using var context = CreateContext(connection);
        var now = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var evaluation = new DateOnly(2026, 8, 28);
        var instrument = new Instrument { FirstObservedAtUtc = now };
        context.Instruments.Add(instrument);
        context.DailyUpdateRuns.Add(new DailyUpdateRun { StartedAtUtc = now, Status = "Succeeded", CompletedAtUtc = now });
        await context.SaveChangesAsync();
        context.DailyBars.Add(new DailyBar
        {
            InstrumentId = instrument.InstrumentId, TradingDate = evaluation, Open = 100m, High = 110m, Low = 90m, Close = 105m, Volume = 1000,
            Source = "YahooFinanceChartApiV8", FetchedAtUtc = now, Revision = 1, Status = "Final",
        });
        await context.SaveChangesAsync();
        var repository = new MarketDataRepository(context);
        var fingerprint = await repository.GetSourceFingerprintAsync("YahooFinanceChartApiV8", instrument.InstrumentId, evaluation, evaluation.AddDays(-30), CancellationToken.None);
        var checkpoint = await repository.StartFetchCheckpointAsync(1, evaluation, "YahooFinanceChartApiV8", instrument.InstrumentId, instrument.InstrumentId.ToString(), evaluation.AddDays(-30), now, TimeSpan.FromHours(1), null, CancellationToken.None);
        await repository.CompleteFetchCheckpointAsync(checkpoint.DailyUpdateFetchCheckpointId, "Succeeded", fingerprint, evaluation, now, CancellationToken.None);

        var reusable = await repository.GetFetchCheckpointDecisionAsync(evaluation, "YahooFinanceChartApiV8", instrument.InstrumentId, instrument.InstrumentId.ToString(), fingerprint, now.AddMinutes(30), CancellationToken.None);
        Assert.Equal(FetchCheckpointDisposition.Reusable, reusable.Disposition);
        var expired = await repository.GetFetchCheckpointDecisionAsync(evaluation, "YahooFinanceChartApiV8", instrument.InstrumentId, instrument.InstrumentId.ToString(), fingerprint, now.AddHours(2), CancellationToken.None);
        Assert.Equal(FetchCheckpointDisposition.RetryExpired, expired.Disposition);

        var original = await context.DailyBars.SingleAsync();
        var correction = new DailyBar
        {
            InstrumentId = instrument.InstrumentId, TradingDate = evaluation, Open = 100m, High = 110m, Low = 90m, Close = 106m, Volume = 1000,
            Source = "YahooFinanceChartApiV8", FetchedAtUtc = now.AddMinutes(1), Revision = 2, SupersedesId = original.DailyBarId, Status = "Corrected",
        };
        context.DailyBars.Add(correction);
        await context.SaveChangesAsync();
        var changedFingerprint = await repository.GetSourceFingerprintAsync("YahooFinanceChartApiV8", instrument.InstrumentId, evaluation, evaluation.AddDays(-30), CancellationToken.None);
        var changed = await repository.GetFetchCheckpointDecisionAsync(evaluation, "YahooFinanceChartApiV8", instrument.InstrumentId, instrument.InstrumentId.ToString(), changedFingerprint, now.AddMinutes(30), CancellationToken.None);
        Assert.Equal(FetchCheckpointDisposition.RetryDataChanged, changed.Disposition);
    }

    [Fact]
    public async Task RefreshPlanning_UsesShortOverlapForNewEvaluationDateWhenHistoryAlreadyExists()
    {
        using var connection = OpenMigratedConnection();
        await using var context = CreateContext(connection);
        var now = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var instrument = new Instrument { FirstObservedAtUtc = now };
        context.Instruments.Add(instrument);
        await context.SaveChangesAsync();
        context.DailyBars.AddRange(
            new DailyBar { InstrumentId = instrument.InstrumentId, TradingDate = new DateOnly(2021, 8, 30), Open = 100m, High = 101m, Low = 99m, Close = 100m, Volume = 1, Source = "YahooFinanceChartApiV8", FetchedAtUtc = now, Revision = 1, Status = "Final" },
            new DailyBar { InstrumentId = instrument.InstrumentId, TradingDate = new DateOnly(2026, 8, 28), Open = 100m, High = 101m, Low = 99m, Close = 100m, Volume = 1, Source = "YahooFinanceChartApiV8", FetchedAtUtc = now, Revision = 1, Status = "Final" });
        await context.SaveChangesAsync();
        var fullStart = new DateTime(2021, 8, 28, 0, 0, 0, DateTimeKind.Utc);
        var targets = await new MarketDataRepository(context).CreateRefreshTargetsAsync([(instrument.InstrumentId, "7203")], new DateOnly(2026, 8, 31), now, fullStart, CancellationToken.None);

        var target = Assert.Single(targets);
        Assert.True(target.RefreshChart);
        Assert.Equal(new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc), target.ChartPeriodStartUtc);
    }

    [Fact]
    public async Task Repository_AppendsOnlyChangedMasterAndMarginRevisions()
    {
        using var connection = OpenMigratedConnection();
        await using var context = CreateContext(connection);
        var observedAt = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var repository = new MarketDataRepository(context);
        var original = new ListedInstrumentSourceRecord("7203", "トヨタ自動車", "Prime", "DomesticCommonStock", "Listed", "Eligible");

        Assert.Equal(1, await repository.ImportInstrumentMasterAsync(new[] { original }, new DateOnly(2026, 8, 30), observedAt, "hash-a", CancellationToken.None));
        Assert.Equal(0, await repository.ImportInstrumentMasterAsync(new[] { original }, new DateOnly(2026, 8, 30), observedAt.AddMinutes(1), "hash-a", CancellationToken.None));
        Assert.Equal(1, await repository.ImportInstrumentMasterAsync(new[] { original with { Name = "トヨタ" } }, new DateOnly(2026, 8, 30), observedAt.AddMinutes(2), "hash-b", CancellationToken.None));
        var instrumentId = await context.InstrumentMasterRevisions.MaxAsync(revision => revision.InstrumentId);

        var margin = new MarginEligibilitySourceRecord("7203", "Eligible", "Unknown", "Eligible", null);
        Assert.Equal(1, await repository.ImportMarginEligibilityAsync(new[] { margin }, new DateOnly(2026, 8, 30), observedAt, CancellationToken.None));
        Assert.Equal(0, await repository.ImportMarginEligibilityAsync(new[] { margin }, new DateOnly(2026, 8, 30), observedAt.AddMinutes(1), CancellationToken.None));
        Assert.Equal(1, await repository.ImportMarginEligibilityAsync(new[] { margin with { ShortSellEligible = "Unknown" } }, new DateOnly(2026, 8, 30), observedAt.AddMinutes(2), CancellationToken.None));
        context.ChangeTracker.Clear();

        var masters = await context.InstrumentMasterRevisions.Where(revision => revision.InstrumentId == instrumentId).OrderBy(revision => revision.Revision).ToListAsync();
        Assert.Equal(masters[0].InstrumentMasterRevisionId, masters[1].SupersedesRevisionId);
        var margins = await context.MarginRegulationRevisions.Where(revision => revision.InstrumentId == instrumentId).OrderBy(revision => revision.Revision).ToListAsync();
        Assert.Equal(margins[0].MarginRegulationRevisionId, margins[1].SupersedesRevisionId);
    }

    [Fact]
    public async Task Schema_RejectsForkedRevisionChains()
    {
        using var connection = OpenMigratedConnection();
        await using var context = CreateContext(connection);
        var observedAt = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var instrument = new Instrument { FirstObservedAtUtc = observedAt };
        context.Instruments.Add(instrument);
        await context.SaveChangesAsync();
        var original = new SwingAdviser.Domain.MarketData.DailyBar
        {
            InstrumentId = instrument.InstrumentId, TradingDate = new DateOnly(2026, 8, 28), Open = 100m, High = 110m, Low = 90m, Close = 105m, Volume = 1000,
            Source = "Test", FetchedAtUtc = observedAt, Revision = 1, Status = "ProviderUnverified",
        };
        context.DailyBars.Add(original);
        await context.SaveChangesAsync();
        context.DailyBars.AddRange(
            CreateBar(instrument.InstrumentId, observedAt, 106m, 2, original.DailyBarId),
            CreateBar(instrument.InstrumentId, observedAt, 107m, 3, original.DailyBarId));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Ingestion_RecordsRateLimitAndContinuesWithOtherInstruments()
    {
        using var connection = OpenMigratedConnection();
        await using var context = CreateContext(connection);
        var observedAt = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var failed = new Instrument { FirstObservedAtUtc = observedAt };
        var succeeded = new Instrument { FirstObservedAtUtc = observedAt };
        context.AddRange(failed, succeeded);
        await context.SaveChangesAsync();

        var service = new MarketDataIngestionService(
            new MarketDataRepository(context),
            new StaticListedIssuesSource(),
            new StaticMarginIssuesSource(),
            new SelectiveYahooSource(),
            new FakeTimeProvider(observedAt));

        var progress = new List<InstrumentRefreshProgress>();
        var results = await service.RefreshInstrumentsAsync(null, new[] { (failed.InstrumentId, "BAD"), (succeeded.InstrumentId, "7203") }, CancellationToken.None,
            new InlineProgress<InstrumentRefreshProgress>(progress.Add));

        Assert.Equal(2, results.Count);
        Assert.Equal(0, results[0].ChartRecordsImported);
        Assert.Equal(1, results[1].ChartRecordsImported);
        Assert.Equal("RateLimit", context.ExternalFetchResults.Single(result => result.InstrumentId == failed.InstrumentId && result.SourceKind == "YahooFinanceChartApiV8").ErrorKind);
        Assert.Single(context.DailyBars);
        Assert.Equal(2, context.FundamentalDataSnapshots.Count());
        Assert.Collection(progress,
            first => Assert.Equal((1, 2, "BAD"), (first.CompletedCount, first.TotalCount, first.LastCompletedCode)),
            second => Assert.Equal((2, 2, "7203"), (second.CompletedCount, second.TotalCount, second.LastCompletedCode)));
    }

    [Fact]
    public async Task Ingestion_FetchesBoundedInstrumentBatchesConcurrentlyWhileKeepingPersistenceSafe()
    {
        using var connection = OpenMigratedConnection();
        await using var context = CreateContext(connection);
        var observedAt = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var instruments = Enumerable.Range(0, 4).Select(_ => new Instrument { FirstObservedAtUtc = observedAt }).ToArray();
        context.Instruments.AddRange(instruments);
        await context.SaveChangesAsync();
        var source = new ParallelObservedYahooSource();
        var service = new MarketDataIngestionService(new MarketDataRepository(context), new StaticListedIssuesSource(), new StaticMarginIssuesSource(), source,
            new FakeTimeProvider(observedAt), maxConcurrentInstrumentFetches: 2);

        var results = await service.RefreshInstrumentsAsync(null, instruments.Select((instrument, index) => (instrument.InstrumentId, $"{7200 + index}")), CancellationToken.None);

        Assert.Equal(4, results.Count);
        Assert.InRange(source.PeakChartRequests, 2, 2);
        Assert.Equal(4, context.DailyBars.Count());
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task JpxClient_ClassifiesHttpAndNetworkFailures()
    {
        using var rateLimitedClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage((HttpStatusCode)429)));
        var source = new JpxListedIssuesClient(rateLimitedClient, new Uri("https://example.invalid/issues.csv"));
        var rateLimit = await Assert.ThrowsAsync<ExternalDataFetchException>(() => source.FetchAsync(CancellationToken.None));
        Assert.Equal("RateLimit", rateLimit.ErrorKind);

        using var networkClient = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline")));
        source = new JpxListedIssuesClient(networkClient, new Uri("https://example.invalid/issues.csv"));
        var network = await Assert.ThrowsAsync<ExternalDataFetchException>(() => source.FetchAsync(CancellationToken.None));
        Assert.Equal("NetworkError", network.ErrorKind);

        using var timeoutClient = new HttpClient(new TimeoutHandler()) { Timeout = TimeSpan.FromMilliseconds(20) };
        source = new JpxListedIssuesClient(timeoutClient, new Uri("https://example.invalid/issues.csv"));
        var timeout = await Assert.ThrowsAsync<ExternalDataFetchException>(() => source.FetchAsync(CancellationToken.None));
        Assert.Equal("Timeout", timeout.ErrorKind);
    }

    private static SqliteConnection OpenMigratedConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        using var context = CreateContext(connection);
        context.Database.Migrate();
        return connection;
    }

    private static SwingAdviserDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<SwingAdviserDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new SwingAdviserDbContext(options);
    }

    private static SwingAdviser.Domain.MarketData.DailyBar CreateBar(int instrumentId, DateTime observedAt, decimal close, int revision, int? supersedesId)
        => new()
        {
            InstrumentId = instrumentId, TradingDate = new DateOnly(2026, 8, 28), Open = 100m, High = 110m, Low = 90m, Close = close, Volume = 1000,
            Source = "Test", FetchedAtUtc = observedAt, Revision = revision, SupersedesId = supersedesId, Status = "ProviderUnverified",
        };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StaticListedIssuesSource : IJpxListedIssuesSource
    {
        public Task<ListedInstrumentSourceSnapshot> FetchAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ListedInstrumentSourceSnapshot(Array.Empty<ListedInstrumentSourceRecord>(), "unused"));
    }

    private sealed class StaticMarginIssuesSource : IJpxMarginIssuesSource
    {
        public Task<IReadOnlyList<MarginEligibilitySourceRecord>> FetchAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MarginEligibilitySourceRecord>>(Array.Empty<MarginEligibilitySourceRecord>());
    }

    private sealed class SelectiveYahooSource : IYahooFinanceSource
    {
        public Task<YahooChartSourceSnapshot> FetchChartAsync(string code, CancellationToken cancellationToken)
        {
            if (code == "BAD") throw new ExternalDataFetchException("RateLimit", "rate limited");
            return Task.FromResult(new YahooChartSourceSnapshot(
                new[] { new YahooDailyBarSourceRecord(new DateOnly(2026, 8, 28), 100m, 110m, 90m, 105m, 1000, null) },
                Array.Empty<CorporateActionSourceRecord>()));
        }

        public Task<FundamentalDataSourceRecord> FetchFundamentalsAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult(new FundamentalDataSourceRecord(null, null, null, null, null));
    }

    private sealed class ParallelObservedYahooSource : IYahooFinanceSource
    {
        private int _activeChartRequests;
        private int _peakChartRequests;

        public int PeakChartRequests => _peakChartRequests;

        public async Task<YahooChartSourceSnapshot> FetchChartAsync(string code, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _activeChartRequests);
            while (true)
            {
                var currentPeak = _peakChartRequests;
                if (currentPeak >= active || Interlocked.CompareExchange(ref _peakChartRequests, active, currentPeak) == currentPeak) break;
            }
            try
            {
                await Task.Delay(50, cancellationToken);
                return new YahooChartSourceSnapshot([new YahooDailyBarSourceRecord(new DateOnly(2026, 8, 28), 100m, 110m, 90m, 105m, 1000, null)], Array.Empty<CorporateActionSourceRecord>());
            }
            finally
            {
                Interlocked.Decrement(ref _activeChartRequests);
            }
        }

        public Task<FundamentalDataSourceRecord> FetchFundamentalsAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult(new FundamentalDataSourceRecord(null, null, null, null, null));
    }

    private sealed class FakeTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
