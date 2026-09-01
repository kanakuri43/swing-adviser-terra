using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.MarketData;

/// <summary>
/// Append-only persistence for externally acquired market data. Equality checks intentionally exclude
/// observation times, so a repeated identical download only creates an external-fetch audit record.
/// </summary>
public sealed class MarketDataRepository
{
    private readonly SwingAdviserDbContext _context;

    public MarketDataRepository(SwingAdviserDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Produces cache-first requests for a finite analysis window. A final evaluation-date bar is
    /// fresh enough for this run; only missing or provisional entries need a provider request.
    /// </summary>
    public async Task<IReadOnlyList<InstrumentRefreshTarget>> CreateRefreshTargetsAsync(
        IEnumerable<(int InstrumentId, string Code)> instruments,
        DateOnly evaluationBarDate,
        DateTime analyzedAtUtc,
        DateTime chartPeriodStartUtc,
        CancellationToken cancellationToken)
    {
        var requested = instruments.ToArray();
        if (requested.Length == 0) return Array.Empty<InstrumentRefreshTarget>();

        var instrumentIds = requested.Select(item => item.InstrumentId).Distinct().ToArray();
        var chartPeriodStartDate = DateOnly.FromDateTime(chartPeriodStartUtc);
        var evaluationBars = await _context.DailyBars
            .Where(entity => instrumentIds.Contains(entity.InstrumentId)
                && entity.TradingDate == evaluationBarDate
                && entity.FetchedAtUtc <= analyzedAtUtc)
            .ToListAsync(cancellationToken);
        var latestEvaluationBarByInstrument = evaluationBars
            .GroupBy(entity => entity.InstrumentId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(entity => entity.Revision).First());
        var cachedRanges = await _context.DailyBars
            .Where(entity => instrumentIds.Contains(entity.InstrumentId)
                && entity.Source == "YahooFinanceChartApiV8"
                && entity.TradingDate >= chartPeriodStartDate
                && entity.TradingDate <= evaluationBarDate
                && entity.FetchedAtUtc <= analyzedAtUtc)
            .GroupBy(entity => entity.InstrumentId)
            .Select(group => new { InstrumentId = group.Key, Earliest = group.Min(item => item.TradingDate) })
            .ToListAsync(cancellationToken);
        var earliestCachedDateByInstrument = cachedRanges.ToDictionary(item => item.InstrumentId, item => item.Earliest);

        return requested.Select(item =>
        {
            latestEvaluationBarByInstrument.TryGetValue(item.InstrumentId, out var evaluationBar);
            earliestCachedDateByInstrument.TryGetValue(item.InstrumentId, out var earliestCachedDate);
            // The provider's requested start can fall on a weekend or exchange holiday. A two-week
            // allowance covers the calendar gap while still rejecting a materially truncated cache.
            var hasWindowCoverage = earliestCachedDate != default && earliestCachedDate <= chartPeriodStartDate.AddDays(14);
            var hasFreshChart = (evaluationBar?.Status is "Final" or "Corrected") && hasWindowCoverage;
            return new InstrumentRefreshTarget(
                item.InstrumentId,
                item.Code,
                !hasFreshChart,
                chartPeriodStartUtc,
                RefreshFundamentals: false);
        }).ToArray();
    }

    public async Task<int> ImportInstrumentMasterAsync(
        IReadOnlyCollection<ListedInstrumentSourceRecord> records,
        DateOnly effectiveAtDate,
        DateTime observedAtUtc,
        string sourceFileHash,
        CancellationToken cancellationToken)
    {
        try
        {
        var sourceRecords = records.ToArray();
        var codes = sourceRecords.Select(record => record.Code).Distinct(StringComparer.Ordinal).ToArray();
        var activeRevisions = await _context.InstrumentMasterRevisions
            .Where(revision => codes.Contains(revision.Code) && revision.Status == "Active")
            .ToListAsync(cancellationToken);
        var instrumentByCode = activeRevisions.GroupBy(revision => revision.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(revision => revision.InstrumentId).Distinct().Take(2).ToArray(), StringComparer.Ordinal);
        var existingInstrumentIds = instrumentByCode.Values.Where(ids => ids.Length == 1).Select(ids => ids[0]).Distinct().ToArray();
        var latestByInstrument = existingInstrumentIds.Length == 0
            ? new Dictionary<int, InstrumentMasterRevision>()
            : (await _context.InstrumentMasterRevisions.Where(revision => existingInstrumentIds.Contains(revision.InstrumentId)).ToListAsync(cancellationToken))
                .GroupBy(revision => revision.InstrumentId).ToDictionary(group => group.Key, group => group.OrderByDescending(revision => revision.Revision).First());
        var newInstruments = new List<(ListedInstrumentSourceRecord Record, Instrument Instrument)>();
        var targets = new List<(ListedInstrumentSourceRecord Record, int InstrumentId, InstrumentMasterRevision? Latest)>();
        foreach (var record in sourceRecords)
        {
            if (instrumentByCode.TryGetValue(record.Code, out var ids) && ids.Length == 1)
            {
                var instrumentId = ids[0];
                targets.Add((record, instrumentId, latestByInstrument[instrumentId]));
            }
            else
            {
                var instrument = new Instrument { FirstObservedAtUtc = observedAtUtc };
                _context.Instruments.Add(instrument);
                newInstruments.Add((record, instrument));
            }
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        if (newInstruments.Count != 0) await _context.SaveChangesAsync(cancellationToken);
        targets.AddRange(newInstruments.Select(item => (item.Record, item.Instrument.InstrumentId, (InstrumentMasterRevision?)null)));
        var imported = 0;
        foreach (var target in targets)
        {
            var record = target.Record;
            var latest = target.Latest;
            if (latest is null || !SameMasterPayload(latest, record, effectiveAtDate, sourceFileHash))
            {
                var revision = new InstrumentMasterRevision
                {
                    InstrumentId = target.InstrumentId,
                    Code = record.Code,
                    Name = record.Name,
                    MarketSegment = record.MarketSegment,
                    InstrumentType = record.InstrumentType,
                    ListedStatus = record.ListedStatus,
                    ScanEligibility = record.ScanEligibility,
                    EffectiveAtDate = effectiveAtDate,
                    AvailableAtUtc = observedAtUtc,
                    Source = "JPX-ListedIssues",
                    SourceFileHash = sourceFileHash,
                    RecordedAtUtc = observedAtUtc,
                    Revision = (latest?.Revision ?? 0) + 1,
                    SupersedesRevisionId = latest?.InstrumentMasterRevisionId,
                    Status = "Active",
                };
                _context.InstrumentMasterRevisions.Add(revision);
                latestByInstrument[target.InstrumentId] = revision;
                imported++;
            }
        }
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return imported;
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    public async Task<int> ImportMarginEligibilityAsync(
        IReadOnlyCollection<MarginEligibilitySourceRecord> records,
        DateOnly effectiveAtDate,
        DateTime observedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
        var sourceRecords = records.ToArray();
        var codes = sourceRecords.Select(record => record.Code).Distinct(StringComparer.Ordinal).ToArray();
        var activeRevisions = await _context.InstrumentMasterRevisions
            .Where(revision => codes.Contains(revision.Code) && revision.Status == "Active")
            .ToListAsync(cancellationToken);
        var instrumentByCode = activeRevisions.GroupBy(revision => revision.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(revision => revision.InstrumentId).Distinct().Take(2).ToArray(), StringComparer.Ordinal);
        var instrumentIds = instrumentByCode.Values.Where(ids => ids.Length == 1).Select(ids => ids[0]).Distinct().ToArray();
        var latestByInstrument = instrumentIds.Length == 0
            ? new Dictionary<int, MarginRegulationRevision>()
            : (await _context.MarginRegulationRevisions.Where(revision => instrumentIds.Contains(revision.InstrumentId)).ToListAsync(cancellationToken))
                .GroupBy(revision => revision.InstrumentId).ToDictionary(group => group.Key, group => group.OrderByDescending(revision => revision.Revision).First());
        var imported = 0;
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        foreach (var record in sourceRecords)
        {
            if (!instrumentByCode.TryGetValue(record.Code, out var matchedIds) || matchedIds.Length != 1) continue;
            var instrumentId = matchedIds[0];
            latestByInstrument.TryGetValue(instrumentId, out var latest);
            if (latest is null || !SameMarginPayload(latest, record, effectiveAtDate))
            {
                var revision = new MarginRegulationRevision
                {
                    InstrumentId = instrumentId,
                    SystemMarginEligible = record.SystemMarginEligible,
                    GeneralMarginEligible = record.GeneralMarginEligible,
                    ShortSellEligible = record.ShortSellEligible,
                    RegulationFlagsJson = record.RegulationFlagsJson,
                    EffectiveAtDate = effectiveAtDate,
                    AvailableAtUtc = observedAtUtc,
                    Source = "JPX-MarginIssues",
                    RecordedAtUtc = observedAtUtc,
                    Revision = (latest?.Revision ?? 0) + 1,
                    SupersedesRevisionId = latest?.MarginRegulationRevisionId,
                    Status = "Active",
                };
                _context.MarginRegulationRevisions.Add(revision);
                latestByInstrument[instrumentId] = revision;
                imported++;
            }
        }
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return imported;
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    public async Task<int> ImportYahooChartAsync(int instrumentId, YahooChartSourceSnapshot snapshot, DateTime fetchedAtUtc, CancellationToken cancellationToken)
    {
        try
        {
        var imported = 0;
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var latestBars = (await _context.DailyBars.Where(entity => entity.InstrumentId == instrumentId).ToListAsync(cancellationToken))
            .GroupBy(entity => entity.TradingDate).ToDictionary(group => group.Key, group => group.OrderByDescending(entity => entity.Revision).First());
        var latestActions = (await _context.CorporateActions.Where(entity => entity.InstrumentId == instrumentId).ToListAsync(cancellationToken))
            .GroupBy(entity => entity.SourceEventId).ToDictionary(group => group.Key, group => group.OrderByDescending(entity => entity.Revision).First(), StringComparer.Ordinal);
        foreach (var bar in snapshot.DailyBars)
        {
            var expectedStatus = bar.TradingDate < ToJstDate(fetchedAtUtc) ? "Final" : "Provisional";
            latestBars.TryGetValue(bar.TradingDate, out var latest);
            if (latest is not null && SameBarPayload(latest, bar, expectedStatus)) continue;
            var revision = new DailyBar
            {
                InstrumentId = instrumentId,
                TradingDate = bar.TradingDate,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                Volume = bar.Volume,
                AdjClose = bar.AdjClose,
                Source = "YahooFinanceChartApiV8",
                FetchedAtUtc = fetchedAtUtc,
                Revision = (latest?.Revision ?? 0) + 1,
                SupersedesId = latest?.DailyBarId,
                Status = expectedStatus,
            };
            _context.DailyBars.Add(revision);
            latestBars[bar.TradingDate] = revision;
            imported++;
        }

        foreach (var action in snapshot.CorporateActions)
        {
            latestActions.TryGetValue(action.SourceEventId, out var latest);
            if (latest is not null && SameActionPayload(latest, action)) continue;
            var revision = new CorporateAction
            {
                InstrumentId = instrumentId,
                ActionType = action.ActionType,
                EffectiveDate = action.EffectiveDate,
                AnnouncedAtUtc = null,
                AvailableAtUtc = fetchedAtUtc,
                FirstObservedAtUtc = latest?.FirstObservedAtUtc ?? fetchedAtUtc,
                SplitRatioNumerator = action.SplitRatioNumerator,
                SplitRatioDenominator = action.SplitRatioDenominator,
                DividendAmountPerShare = action.DividendAmountPerShare,
                Currency = action.Currency,
                SourceEventId = action.SourceEventId,
                Source = "YahooFinanceChartApiV8",
                RecordedAtUtc = fetchedAtUtc,
                Revision = (latest?.Revision ?? 0) + 1,
                SupersedesId = latest?.CorporateActionId,
                Status = "PointInTimeUnverified",
            };
            _context.CorporateActions.Add(revision);
            latestActions[action.SourceEventId] = revision;
            imported++;
        }
        var coverage = await _context.DailyBarHistoryCoverages
            .Where(entity => entity.InstrumentId == instrumentId && entity.Source == "YahooFinanceChartApiV8")
            .OrderByDescending(entity => entity.Revision)
            .FirstOrDefaultAsync(cancellationToken);
        var snapshotEarliest = snapshot.DailyBars.Min(bar => bar.TradingDate);
        var snapshotLatest = snapshot.DailyBars.Max(bar => bar.TradingDate);
        var retainFullHistoryConfirmation = coverage?.FullHistoryConfirmed == true && !snapshot.FullHistoryConfirmed;
        var earliest = retainFullHistoryConfirmation
            ? coverage!.EarliestReturnedDate
            : snapshotEarliest;
        var latestDate = retainFullHistoryConfirmation
            ? (coverage!.LatestReturnedDate > snapshotLatest ? coverage.LatestReturnedDate : snapshotLatest)
            : snapshotLatest;
        var fullHistoryConfirmed = snapshot.FullHistoryConfirmed || coverage?.FullHistoryConfirmed == true;
        var coverageStatus = fullHistoryConfirmed ? "Complete" : "Incomplete";
        if (coverage is null || coverage.EarliestReturnedDate != earliest || coverage.LatestReturnedDate != latestDate || coverage.FullHistoryConfirmed != fullHistoryConfirmed || coverage.Status != coverageStatus)
        {
            _context.DailyBarHistoryCoverages.Add(new DailyBarHistoryCoverage
            {
                InstrumentId = instrumentId, Source = "YahooFinanceChartApiV8", EarliestReturnedDate = earliest, LatestReturnedDate = latestDate,
                FullHistoryConfirmed = fullHistoryConfirmed, ObservedAtUtc = fetchedAtUtc, Revision = (coverage?.Revision ?? 0) + 1,
                SupersedesId = coverage?.DailyBarHistoryCoverageId, Status = coverageStatus,
            });
        }
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return imported;
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    public async Task AddFundamentalSnapshotAsync(int instrumentId, FundamentalDataSourceRecord sourceRecord, DateTime fetchedAtUtc, CancellationToken cancellationToken)
    {
        try
        {
        _context.FundamentalDataSnapshots.Add(new FundamentalDataSnapshot
        {
            InstrumentId = instrumentId,
            FetchedAtUtc = fetchedAtUtc,
            Source = "YahooFinanceQuoteApiV7",
            Per = sourceRecord.Per,
            Pbr = sourceRecord.Pbr,
            MarketCap = sourceRecord.MarketCap,
            DividendYield = sourceRecord.DividendYield,
            AdditionalMetricsJson = sourceRecord.AdditionalMetricsJson,
        });
        await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    public async Task RecordFetchResultAsync(int? dailyUpdateRunId, string sourceKind, int? instrumentId, string status, string? errorKind, string? errorMessage, int? recordCount, DateTime attemptedAtUtc, CancellationToken cancellationToken)
    {
        try
        {
        _context.ExternalFetchResults.Add(new ExternalFetchResult
        {
            DailyUpdateRunId = dailyUpdateRunId,
            SourceKind = sourceKind,
            InstrumentId = instrumentId,
            Status = status,
            ErrorKind = errorKind,
            ErrorMessage = errorMessage,
            RecordCount = recordCount,
            AttemptedAtUtc = attemptedAtUtc,
        });
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException sqlite && sqlite.SqliteErrorCode == 5)
        {
            throw new ExternalDataFetchException("DatabaseLocked", "SQLite was locked while recording the external fetch.", exception);
        }
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    private static bool SameMasterPayload(InstrumentMasterRevision entity, ListedInstrumentSourceRecord record, DateOnly effectiveAtDate, string sourceHash)
        => entity.Code == record.Code && entity.Name == record.Name && entity.MarketSegment == record.MarketSegment && entity.InstrumentType == record.InstrumentType && entity.ListedStatus == record.ListedStatus && entity.ScanEligibility == record.ScanEligibility && entity.EffectiveAtDate == effectiveAtDate && entity.SourceFileHash == sourceHash && entity.Status == "Active";

    private static bool SameMarginPayload(MarginRegulationRevision entity, MarginEligibilitySourceRecord record, DateOnly effectiveAtDate)
        => entity.SystemMarginEligible == record.SystemMarginEligible && entity.GeneralMarginEligible == record.GeneralMarginEligible && entity.ShortSellEligible == record.ShortSellEligible && entity.RegulationFlagsJson == record.RegulationFlagsJson && entity.EffectiveAtDate == effectiveAtDate && entity.Status == "Active";

    private static bool SameBarPayload(DailyBar entity, YahooDailyBarSourceRecord record, string expectedStatus)
        => entity.Open == record.Open && entity.High == record.High && entity.Low == record.Low && entity.Close == record.Close && entity.Volume == record.Volume && entity.AdjClose == record.AdjClose && entity.Status == expectedStatus;

    private static bool SameActionPayload(CorporateAction entity, CorporateActionSourceRecord record)
        => entity.ActionType == record.ActionType && entity.EffectiveDate == record.EffectiveDate && entity.SplitRatioNumerator == record.SplitRatioNumerator && entity.SplitRatioDenominator == record.SplitRatioDenominator && entity.DividendAmountPerShare == record.DividendAmountPerShare && entity.Currency == record.Currency && entity.Status == "PointInTimeUnverified";

    private static DateOnly ToJstDate(DateTime utc)
    {
        try { return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"))); }
        catch (TimeZoneNotFoundException) { return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"))); }
    }
}
