using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwingAdviser.Infrastructure.MarketData;

/// <summary>Yahoo Finance chart and quote client. Provider data remains isolated from application logic.</summary>
public sealed class YahooFinanceClient : IIncrementalYahooFinanceSource
{
    private static readonly Regex SafeCode = new("^[0-9A-Za-z]+$", RegexOptions.CultureInvariant);
    private static readonly SemaphoreSlim ChartRequestStartGate = new(1, 1);
    private static DateTime _nextChartRequestStartUtc = DateTime.MinValue;
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public YahooFinanceClient(HttpClient httpClient, Uri? baseUri = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUri = baseUri ?? new Uri("https://query1.finance.yahoo.com/");
    }

    public async Task<YahooChartSourceSnapshot> FetchChartAsync(string code, CancellationToken cancellationToken)
        => await FetchChartAsync(code, null, cancellationToken);

    public async Task<YahooChartSourceSnapshot> FetchChartAsync(string code, DateTime? periodStartUtc, CancellationToken cancellationToken)
    {
        var symbol = ToSymbol(code);
        var period1 = periodStartUtc is null
            ? "0"
            : new DateTimeOffset(DateTime.SpecifyKind(periodStartUtc.Value, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var uri = new Uri(_baseUri, $"v8/finance/chart/{symbol}?period1={period1}&period2={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}&interval=1d&events=div%2Csplits");
        await WaitForChartRequestStartAsync(cancellationToken);
        var body = await HttpFetch.ReadTextAsync(_httpClient, uri, cancellationToken);
        return YahooFinanceParser.ParseChart(body, fullHistoryRequested: periodStartUtc is null);
    }

    public async Task<FundamentalDataSourceRecord> FetchFundamentalsAsync(string code, CancellationToken cancellationToken)
    {
        var symbol = ToSymbol(code);
        var uri = new Uri(_baseUri, $"v7/finance/quote?symbols={symbol}");
        var body = await HttpFetch.ReadTextAsync(_httpClient, uri, cancellationToken);
        return YahooFinanceParser.ParseFundamentals(body);
    }

    private static string ToSymbol(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !SafeCode.IsMatch(code))
        {
            throw new ArgumentException("A Yahoo Finance Japan code must contain only letters and digits.", nameof(code));
        }
        return $"{code.ToUpperInvariant()}.T";
    }

    /// <summary>
    /// Keeps concurrent downloads in flight while spacing their starts at the same five-per-second
    /// cadence used by the reference scanner, reducing avoidable Yahoo rate-limit responses.
    /// </summary>
    private static async Task WaitForChartRequestStartAsync(CancellationToken cancellationToken)
    {
        await ChartRequestStartGate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var delay = _nextChartRequestStartUtc - now;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            _nextChartRequestStartUtc = DateTime.UtcNow.AddMilliseconds(200);
        }
        finally
        {
            ChartRequestStartGate.Release();
        }
    }
}

public static class YahooFinanceParser
{
    public static YahooChartSourceSnapshot ParseChart(string json, bool fullHistoryRequested = true)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var result = document.RootElement.GetProperty("chart").GetProperty("result");
            if (result.GetArrayLength() != 1)
            {
                throw new ExternalDataFetchException("InvalidData", "Yahoo chart response had no result.");
            }

            var chart = result[0];
            var timestamps = chart.GetProperty("timestamp").EnumerateArray().Select(value => value.GetInt64()).ToArray();
            var quote = chart.GetProperty("indicators").GetProperty("quote")[0];
            var open = quote.GetProperty("open").EnumerateArray().ToArray();
            var high = quote.GetProperty("high").EnumerateArray().ToArray();
            var low = quote.GetProperty("low").EnumerateArray().ToArray();
            var close = quote.GetProperty("close").EnumerateArray().ToArray();
            var volume = quote.GetProperty("volume").EnumerateArray().ToArray();
            var adjClose = chart.TryGetProperty("indicators", out var indicators) && indicators.TryGetProperty("adjclose", out var adjusted)
                ? adjusted[0].GetProperty("adjclose").EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();
            if (new[] { open.Length, high.Length, low.Length, close.Length, volume.Length }.Any(length => length != timestamps.Length))
            {
                throw new ExternalDataFetchException("InvalidData", "Yahoo chart arrays have inconsistent lengths.");
            }

            var bars = new List<YahooDailyBarSourceRecord>();
            var containsInvalidBar = false;
            for (var index = 0; index < timestamps.Length; index++)
            {
                if (!TryDecimal(open[index], out var openValue) || !TryDecimal(high[index], out var highValue) || !TryDecimal(low[index], out var lowValue) || !TryDecimal(close[index], out var closeValue) || !TryLong(volume[index], out var volumeValue))
                {
                    containsInvalidBar = true;
                    continue;
                }
                if (openValue <= 0 || highValue <= 0 || lowValue <= 0 || closeValue <= 0 || volumeValue < 0 || highValue < lowValue || openValue < lowValue || openValue > highValue || closeValue < lowValue || closeValue > highValue)
                {
                    containsInvalidBar = true;
                    continue;
                }

                bars.Add(new YahooDailyBarSourceRecord(ToJstDate(timestamps[index]), openValue, highValue, lowValue, closeValue, volumeValue,
                    index < adjClose.Length && TryDecimal(adjClose[index], out var adjustedValue) ? adjustedValue : null));
            }
            if (bars.Count == 0)
            {
                throw new ExternalDataFetchException("InvalidData", "Yahoo chart response contained no valid daily bars.");
            }

            return new YahooChartSourceSnapshot(bars, ParseCorporateActions(chart), fullHistoryRequested && !containsInvalidBar);
        }
        catch (ExternalDataFetchException)
        {
            throw;
        }
        catch (KeyNotFoundException exception)
        {
            throw new ExternalDataFetchException("InvalidData", "Yahoo chart response is missing a required field.", exception);
        }
        catch (JsonException exception)
        {
            throw new ExternalDataFetchException("InvalidData", "Yahoo chart response is not valid JSON.", exception);
        }
    }

    public static FundamentalDataSourceRecord ParseFundamentals(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var results = document.RootElement.GetProperty("quoteResponse").GetProperty("result");
            if (results.GetArrayLength() != 1)
            {
                throw new ExternalDataFetchException("InvalidData", "Yahoo quote response had no result.");
            }
            var quote = results[0];
            var additional = new Dictionary<string, decimal?>();
            foreach (var name in new[] { "forwardPE", "trailingEps", "bookValue", "enterpriseValue" })
            {
                additional[name] = TryPropertyDecimal(quote, name);
            }
            return new FundamentalDataSourceRecord(
                TryPropertyDecimal(quote, "trailingPE"),
                TryPropertyDecimal(quote, "priceToBook"),
                TryPropertyDecimal(quote, "marketCap"),
                TryPropertyDecimal(quote, "trailingAnnualDividendYield"),
                JsonSerializer.Serialize(additional));
        }
        catch (ExternalDataFetchException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ExternalDataFetchException("InvalidData", "Yahoo quote response is not valid JSON.", exception);
        }
    }

    private static IReadOnlyList<CorporateActionSourceRecord> ParseCorporateActions(JsonElement chart)
    {
        if (!chart.TryGetProperty("events", out var events)) return Array.Empty<CorporateActionSourceRecord>();
        var records = new List<CorporateActionSourceRecord>();
        if (events.TryGetProperty("splits", out var splits))
        {
            foreach (var property in splits.EnumerateObject())
            {
                var action = property.Value;
                if (!action.TryGetProperty("date", out var date) || !action.TryGetProperty("numerator", out var numerator) || !action.TryGetProperty("denominator", out var denominator)) continue;
                var eventAt = DateTimeOffset.FromUnixTimeSeconds(date.GetInt64()).UtcDateTime;
                records.Add(new CorporateActionSourceRecord("Split", ToJstDate(date.GetInt64()), eventAt, numerator.GetInt32(), denominator.GetInt32(), null, null, $"YahooFinanceChartApiV8:split:{property.Name}"));
            }
        }
        if (events.TryGetProperty("dividends", out var dividends))
        {
            foreach (var property in dividends.EnumerateObject())
            {
                var action = property.Value;
                if (!action.TryGetProperty("date", out var date) || !action.TryGetProperty("amount", out var amount) || !TryDecimal(amount, out var amountValue)) continue;
                var eventAt = DateTimeOffset.FromUnixTimeSeconds(date.GetInt64()).UtcDateTime;
                records.Add(new CorporateActionSourceRecord("CashDividend", ToJstDate(date.GetInt64()), eventAt, null, null, amountValue, "JPY", $"YahooFinanceChartApiV8:dividend:{property.Name}"));
            }
        }
        return records;
    }

    private static DateOnly ToJstDate(long unixTime)
    {
        var jst = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unixTime), TokyoTimeZone);
        return DateOnly.FromDateTime(jst.DateTime);
    }

    private static TimeZoneInfo TokyoTimeZone
    {
        get
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"); }
        }
    }

    private static decimal? TryPropertyDecimal(JsonElement objectElement, string propertyName)
        => objectElement.TryGetProperty(propertyName, out var value) && TryDecimal(value, out var decimalValue) ? decimalValue : null;

    private static bool TryDecimal(JsonElement value, out decimal result)
    {
        result = default;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out result)
            || value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryLong(JsonElement value, out long result)
    {
        result = default;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out result)
            || value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }
}
