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

    public async Task<int> ImportInstrumentMasterAsync(
        IReadOnlyCollection<ListedInstrumentSourceRecord> records,
        DateOnly effectiveAtDate,
        DateTime observedAtUtc,
        string sourceFileHash,
        CancellationToken cancellationToken)
    {
        var imported = 0;
        foreach (var record in records)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            var candidates = await _context.InstrumentMasterRevisions
                .Where(revision => revision.Code == record.Code && revision.Status == "Active")
                .OrderByDescending(revision => revision.RecordedAtUtc)
                .Select(revision => revision.InstrumentId)
                .Distinct()
                .Take(2)
                .ToListAsync(cancellationToken);
            var instrumentId = candidates.Count == 1 ? candidates[0] : 0;
            InstrumentMasterRevision? latest = null;
            if (instrumentId == 0)
            {
                var instrument = new Instrument { FirstObservedAtUtc = observedAtUtc };
                _context.Instruments.Add(instrument);
                await _context.SaveChangesAsync(cancellationToken);
                instrumentId = instrument.InstrumentId;
            }
            else
            {
                latest = await _context.InstrumentMasterRevisions
                    .Where(revision => revision.InstrumentId == instrumentId)
                    .OrderByDescending(revision => revision.Revision)
                    .SingleOrDefaultAsync(cancellationToken);
            }

            if (latest is null || !SameMasterPayload(latest, record, effectiveAtDate, sourceFileHash))
            {
                _context.InstrumentMasterRevisions.Add(new InstrumentMasterRevision
                {
                    InstrumentId = instrumentId,
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
                });
                await _context.SaveChangesAsync(cancellationToken);
                imported++;
            }
            await transaction.CommitAsync(cancellationToken);
        }
        return imported;
    }

    public async Task<int> ImportMarginEligibilityAsync(
        IReadOnlyCollection<MarginEligibilitySourceRecord> records,
        DateOnly effectiveAtDate,
        DateTime observedAtUtc,
        CancellationToken cancellationToken)
    {
        var imported = 0;
        foreach (var record in records)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            var instrumentIds = await _context.InstrumentMasterRevisions
                .Where(revision => revision.Code == record.Code && revision.Status == "Active")
                .OrderByDescending(revision => revision.RecordedAtUtc)
                .Select(revision => revision.InstrumentId)
                .Distinct()
                .Take(2)
                .ToListAsync(cancellationToken);
            if (instrumentIds.Count != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            var instrumentId = instrumentIds[0];
            var latest = await _context.MarginRegulationRevisions
                .Where(revision => revision.InstrumentId == instrumentId)
                .OrderByDescending(revision => revision.Revision)
                .SingleOrDefaultAsync(cancellationToken);
            if (latest is null || !SameMarginPayload(latest, record, effectiveAtDate))
            {
                _context.MarginRegulationRevisions.Add(new MarginRegulationRevision
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
                });
                await _context.SaveChangesAsync(cancellationToken);
                imported++;
            }
            await transaction.CommitAsync(cancellationToken);
        }
        return imported;
    }

    public async Task<int> ImportYahooChartAsync(int instrumentId, YahooChartSourceSnapshot snapshot, DateTime fetchedAtUtc, CancellationToken cancellationToken)
    {
        var imported = 0;
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        foreach (var bar in snapshot.DailyBars)
        {
            var expectedStatus = bar.TradingDate < ToJstDate(fetchedAtUtc) ? "Final" : "Provisional";
            var latest = await _context.DailyBars
                .Where(entity => entity.InstrumentId == instrumentId && entity.TradingDate == bar.TradingDate)
                .OrderByDescending(entity => entity.Revision)
                .SingleOrDefaultAsync(cancellationToken);
            if (latest is not null && SameBarPayload(latest, bar, expectedStatus)) continue;
            _context.DailyBars.Add(new DailyBar
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
            });
            imported++;
        }

        foreach (var action in snapshot.CorporateActions)
        {
            var latest = await _context.CorporateActions
                .Where(entity => entity.InstrumentId == instrumentId && entity.SourceEventId == action.SourceEventId)
                .OrderByDescending(entity => entity.Revision)
                .SingleOrDefaultAsync(cancellationToken);
            if (latest is not null && SameActionPayload(latest, action)) continue;
            _context.CorporateActions.Add(new CorporateAction
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
            });
            imported++;
        }
        var coverage = await _context.DailyBarHistoryCoverages
            .Where(entity => entity.InstrumentId == instrumentId && entity.Source == "YahooFinanceChartApiV8")
            .OrderByDescending(entity => entity.Revision)
            .FirstOrDefaultAsync(cancellationToken);
        var earliest = snapshot.DailyBars.Min(bar => bar.TradingDate);
        var latestDate = snapshot.DailyBars.Max(bar => bar.TradingDate);
        var coverageStatus = snapshot.FullHistoryConfirmed ? "Complete" : "Incomplete";
        if (coverage is null || coverage.EarliestReturnedDate != earliest || coverage.LatestReturnedDate != latestDate || coverage.FullHistoryConfirmed != snapshot.FullHistoryConfirmed || coverage.Status != coverageStatus)
        {
            _context.DailyBarHistoryCoverages.Add(new DailyBarHistoryCoverage
            {
                InstrumentId = instrumentId, Source = "YahooFinanceChartApiV8", EarliestReturnedDate = earliest, LatestReturnedDate = latestDate,
                FullHistoryConfirmed = snapshot.FullHistoryConfirmed, ObservedAtUtc = fetchedAtUtc, Revision = (coverage?.Revision ?? 0) + 1,
                SupersedesId = coverage?.DailyBarHistoryCoverageId, Status = coverageStatus,
            });
        }
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return imported;
    }

    public async Task AddFundamentalSnapshotAsync(int instrumentId, FundamentalDataSourceRecord sourceRecord, DateTime fetchedAtUtc, CancellationToken cancellationToken)
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

    public async Task RecordFetchResultAsync(int? dailyUpdateRunId, string sourceKind, int? instrumentId, string status, string? errorKind, string? errorMessage, int? recordCount, DateTime attemptedAtUtc, CancellationToken cancellationToken)
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
