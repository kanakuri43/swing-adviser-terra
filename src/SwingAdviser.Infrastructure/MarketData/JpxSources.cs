using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SwingAdviser.Infrastructure.MarketData;

/// <summary>
/// Retrieves a JPX listed-issues export in CSV or TSV form.  The export URI is configurable because
/// JPX changes download locations and the Data Portal may require a user-selected CSV export.
/// </summary>
public sealed class JpxListedIssuesClient : IJpxListedIssuesSource
{
    private readonly HttpClient _httpClient;
    private readonly Uri _sourceUri;

    public JpxListedIssuesClient(HttpClient httpClient, Uri sourceUri)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _sourceUri = sourceUri ?? throw new ArgumentNullException(nameof(sourceUri));
    }

    public async Task<ListedInstrumentSourceSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var body = await HttpFetch.ReadTextAsync(_httpClient, _sourceUri, cancellationToken);
        return new ListedInstrumentSourceSnapshot(JpxListedIssuesParser.Parse(body), JpxListedIssuesParser.ComputeSourceFileHash(body));
    }
}

public static class JpxListedIssuesParser
{
    public static IReadOnlyList<ListedInstrumentSourceRecord> Parse(string text)
    {
        var rows = DelimitedText.Read(text);
        if (rows.Count < 2)
        {
            throw new ExternalDataFetchException("InvalidData", "JPX listed-issues export has no data rows.");
        }

        var header = DelimitedText.HeaderIndex(rows[0]);
        var codeColumn = DelimitedText.RequiredColumn(header, "コード", "code");
        var nameColumn = DelimitedText.RequiredColumn(header, "銘柄名", "name");
        var segmentColumn = DelimitedText.RequiredColumn(header, "市場商品区分", "市場区分", "marketsegment");
        var result = new List<ListedInstrumentSourceRecord>();

        foreach (var row in rows.Skip(1))
        {
            var code = DelimitedText.Value(row, codeColumn).Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            var name = DelimitedText.Value(row, nameColumn).Trim();
            var segmentAndType = DelimitedText.Value(row, segmentColumn).Trim();
            var (segment, type) = ClassifySegmentAndType(segmentAndType);
            result.Add(new ListedInstrumentSourceRecord(
                code,
                name,
                segment,
                type,
                "Listed",
                type == "DomesticCommonStock" && segment is "Prime" or "Standard" or "Growth" ? "Eligible" : "Ineligible"));
        }

        if (result.Count == 0)
        {
            throw new ExternalDataFetchException("InvalidData", "JPX listed-issues export did not contain a usable instrument code.");
        }

        return result;
    }

    public static string ComputeSourceFileHash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static (string Segment, string Type) ClassifySegmentAndType(string value)
    {
        var segment = value.Contains("プライム", StringComparison.Ordinal) ? "Prime"
            : value.Contains("スタンダード", StringComparison.Ordinal) ? "Standard"
            : value.Contains("グロース", StringComparison.Ordinal) ? "Growth"
            : "Other";
        var type = value.Contains("内国株", StringComparison.Ordinal) ? "DomesticCommonStock"
            : value.Contains("ETF", StringComparison.OrdinalIgnoreCase) ? "ETF"
            : value.Contains("ETN", StringComparison.OrdinalIgnoreCase) ? "ETN"
            : value.Contains("REIT", StringComparison.OrdinalIgnoreCase) ? "REIT"
            : "Other";
        return (segment, type);
    }
}

/// <summary>Parses the JPX official margin/loanable-issues page without assuming missing issues are ineligible.</summary>
public sealed class JpxMarginIssuesClient : IJpxMarginIssuesSource
{
    private readonly HttpClient _httpClient;
    private readonly Uri _sourceUri;

    public JpxMarginIssuesClient(HttpClient httpClient, Uri sourceUri)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _sourceUri = sourceUri ?? throw new ArgumentNullException(nameof(sourceUri));
    }

    public async Task<IReadOnlyList<MarginEligibilitySourceRecord>> FetchAsync(CancellationToken cancellationToken)
    {
        var body = await HttpFetch.ReadTextAsync(_httpClient, _sourceUri, cancellationToken);
        return JpxMarginIssuesParser.Parse(body);
    }
}

public static class JpxMarginIssuesParser
{
    private static readonly Regex HeadingRegex = new("<(?:h2|h3)[^>]*>\\s*(?<heading>.*?)\\s*</(?:h2|h3)>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex TableRegex = new("<table[^>]*>(?<table>.*?)</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex RowRegex = new("<tr[^>]*>(?<row>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex CellRegex = new("<t[dh][^>]*>(?<cell>.*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static IReadOnlyList<MarginEligibilitySourceRecord> Parse(string html)
    {
        var selected = new Dictionary<string, (bool System, bool Loan)>(StringComparer.OrdinalIgnoreCase);
        var headingPositions = HeadingRegex.Matches(html);
        foreach (Match heading in headingPositions)
        {
            var headingText = Clean(heading.Groups["heading"].Value);
            var isSystem = headingText.Contains("制度信用", StringComparison.Ordinal) && headingText.Contains("選定銘柄", StringComparison.Ordinal);
            var isLoan = headingText.Contains("貸借", StringComparison.Ordinal) && headingText.Contains("選定銘柄", StringComparison.Ordinal);
            if (!isSystem && !isLoan)
            {
                continue;
            }

            var nextHeading = heading.Index + heading.Length;
            var table = TableRegex.Match(html, nextHeading);
            if (!table.Success || (headingPositions.Cast<Match>().FirstOrDefault(match => match.Index > heading.Index)?.Index ?? int.MaxValue) < table.Index)
            {
                continue;
            }

            foreach (Match row in RowRegex.Matches(table.Groups["table"].Value))
            {
                var cells = CellRegex.Matches(row.Groups["row"].Value).Select(match => Clean(match.Groups["cell"].Value)).ToArray();
                if (cells.Length < 3 || cells.Any(cell => cell.Equals("コード", StringComparison.Ordinal)))
                {
                    continue;
                }

                var code = cells.FirstOrDefault(cell => Regex.IsMatch(cell, "^[0-9A-Za-z]+$", RegexOptions.CultureInvariant));
                if (code is null)
                {
                    continue;
                }

                selected.TryGetValue(code, out var current);
                selected[code] = (current.System || isSystem, current.Loan || isLoan);
            }
        }

        if (selected.Count == 0)
        {
            throw new ExternalDataFetchException("InvalidData", "JPX margin/loanable-issues page had no recognizable issue rows.");
        }

        return selected.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new MarginEligibilitySourceRecord(
            pair.Key,
            pair.Value.System ? "Eligible" : "Unknown",
            "Unknown",
            pair.Value.Loan ? "Eligible" : "Unknown",
            null)).ToArray();
    }

    private static string Clean(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", string.Empty)).Replace("※", string.Empty).Trim();
}

internal static class HttpFetch
{
    public static async Task<string> ReadTextAsync(HttpClient httpClient, Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ExternalDataFetchException(response.StatusCode == (HttpStatusCode)429 ? "RateLimit" : "HttpError", $"External source returned HTTP {(int)response.StatusCode}.");
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (ExternalDataFetchException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalDataFetchException("Timeout", "External source request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new ExternalDataFetchException("NetworkError", "External source request failed.", exception);
        }
    }
}

internal static class DelimitedText
{
    public static List<string[]> Read(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ExternalDataFetchException("InvalidData", "The source response was empty.");
        }

        var delimiter = text.IndexOf('\t') >= 0 ? '\t' : ',';
        var rows = new List<string[]>();
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"')
                {
                    current.Append(character);
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (!quoted && character == delimiter)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else if (!quoted && (character == '\n' || character == '\r'))
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                values.Add(current.ToString());
                current.Clear();
                if (values.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(values.ToArray());
                values.Clear();
            }
            else
            {
                current.Append(character);
            }
        }
        values.Add(current.ToString());
        if (values.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(values.ToArray());
        return rows;
    }

    public static Dictionary<string, int> HeaderIndex(string[] header) => header
        .Select((value, index) => new { Key = Normalize(value), index })
        .GroupBy(item => item.Key, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);

    public static int RequiredColumn(IReadOnlyDictionary<string, int> header, params string[] alternatives)
    {
        foreach (var alternative in alternatives)
        {
            if (header.TryGetValue(Normalize(alternative), out var index)) return index;
        }
        throw new ExternalDataFetchException("InvalidData", $"Required JPX column is missing: {alternatives[0]}.");
    }

    public static string Value(string[] row, int index) => index < row.Length ? row[index] : string.Empty;

    private static string Normalize(string value) => value.Trim().TrimStart('\uFEFF').Replace(" ", string.Empty).Replace("　", string.Empty).Replace("・", string.Empty).ToLowerInvariant();
}
