using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwingAdviser.Application.Analysis;

/// <summary>Strict semantic validation for the only AI result version accepted by the initial release.</summary>
public static class AiResultV1Parser
{
    public const string SchemaVersion = "ai-result-v1";

    public static AiResultParseResult Parse(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return AiResultParseResult.Invalid("InvalidResponse", "The CLI returned no JSON response.");
        try
        {
            using var document = JsonDocument.Parse(response);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return AiResultParseResult.Invalid("InvalidResponse", "The response root must be an object.");
            var root = document.RootElement;
            if (!TryString(root, "schemaVersion", out var version) || version != SchemaVersion)
                return AiResultParseResult.Invalid("UnsupportedResponse", "schemaVersion must exactly equal ai-result-v1.");
            if (!TryString(root, "outcome", out var outcome) || (outcome != "Succeeded" && outcome != "InsufficientInformation"))
                return AiResultParseResult.Invalid("InvalidResponse", "outcome must be Succeeded or InsufficientInformation.");
            if (!TryRequiredText(root, "summary", 2000, out var summary, out var error)) return AiResultParseResult.Invalid("InvalidResponse", error);
            if (!TryUtc(root, "checkedAtUtc", out var checkedAtUtc, out error)) return AiResultParseResult.Invalid("InvalidResponse", error);
            if (!TryOptionalText(root, "technicalView", 2000, out var technicalView, out error) || !TryOptionalText(root, "fundamentalView", 2000, out var fundamentalView, out error))
                return AiResultParseResult.Invalid("InvalidResponse", error);
            if (!TryEvidence(root, "positiveFactors", "Positive", out var positive, out error) || !TryEvidence(root, "riskFactors", "Risk", out var risks, out error) || !TryEvidence(root, "invalidationConditions", "Invalidation", out var invalidations, out error))
                return AiResultParseResult.Invalid("InvalidResponse", error);
            if (!TrySources(root, checkedAtUtc, out var sources, out error)) return AiResultParseResult.Invalid("InvalidResponse", error);

            if (!TryNullableLabel(root, "verdict", ["Bullish", "Neutral", "Bearish"], out var verdict, out error) || !TryNullableLabel(root, "confidence", ["High", "Medium", "Low"], out var confidence, out error))
                return AiResultParseResult.Invalid("InvalidResponse", error);
            if (!CitationsAreValid([.. positive, .. risks, .. invalidations], sources.Count, out error)) return AiResultParseResult.Invalid("InvalidResponse", error);

            if (outcome == "Succeeded")
            {
                if (verdict is null || confidence is null || sources.Count == 0) return AiResultParseResult.Invalid("InvalidResponse", "Succeeded requires verdict, confidence, and at least one source.");
            }
            else if (verdict is not null || confidence is not null || technicalView is not null || fundamentalView is not null || positive.Count != 0 || risks.Count != 0 || invalidations.Count != 0)
                return AiResultParseResult.Invalid("InvalidResponse", "InsufficientInformation must contain only its summary and checked timestamp.");

            var value = new AiResultV1(outcome, verdict, confidence, summary, technicalView, fundamentalView, positive, risks, invalidations, checkedAtUtc, sources);
            return AiResultParseResult.Valid(value, Convert.ToHexString(SHA256.HashData(Canonicalize(value))).ToLowerInvariant());
        }
        catch (JsonException exception) { return AiResultParseResult.Invalid("InvalidResponse", $"Invalid JSON: {exception.Message}"); }
    }

    public static byte[] Canonicalize(AiResultV1 value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject(); writer.WriteString("schemaVersion", SchemaVersion); writer.WriteString("outcome", value.Outcome);
        WriteNullable(writer, "verdict", value.Verdict); WriteNullable(writer, "confidence", value.Confidence); writer.WriteString("summary", value.Summary);
        WriteNullable(writer, "technicalView", value.TechnicalView); WriteNullable(writer, "fundamentalView", value.FundamentalView);
        WriteEvidence(writer, "positiveFactors", value.PositiveFactors); WriteEvidence(writer, "riskFactors", value.RiskFactors); WriteEvidence(writer, "invalidationConditions", value.InvalidationConditions);
        writer.WriteString("checkedAtUtc", value.CheckedAtUtc.ToString("O", CultureInfo.InvariantCulture)); writer.WritePropertyName("sources"); writer.WriteStartArray();
        foreach (var source in value.Sources) { writer.WriteStartObject(); writer.WriteString("url", source.Url); WriteNullable(writer, "title", source.Title); if (source.PublishedAtUtc is null) writer.WriteNull("publishedAtUtc"); else writer.WriteString("publishedAtUtc", source.PublishedAtUtc.Value.ToString("O", CultureInfo.InvariantCulture)); writer.WriteString("retrievedAtUtc", source.RetrievedAtUtc.ToString("O", CultureInfo.InvariantCulture)); writer.WriteEndObject(); }
        writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush(); return buffer.WrittenSpan.ToArray();
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value) { if (value is null) writer.WriteNull(name); else writer.WriteString(name, value); }
    private static void WriteEvidence(Utf8JsonWriter writer, string name, IReadOnlyList<AiEvidenceItem> items) { writer.WritePropertyName(name); writer.WriteStartArray(); foreach (var item in items) { writer.WriteStartObject(); writer.WriteString("text", item.Text); writer.WritePropertyName("sourceOrdinals"); writer.WriteStartArray(); foreach (var ordinal in item.SourceOrdinals) writer.WriteNumberValue(ordinal); writer.WriteEndArray(); writer.WriteEndObject(); } writer.WriteEndArray(); }
    private static bool TryString(JsonElement root, string name, out string value) { value = string.Empty; return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value = element.GetString()!.Trim()); }
    private static bool TryRequiredText(JsonElement root, string name, int max, out string value, out string error) { error = string.Empty; if (!TryString(root, name, out value)) { error = $"{name} must be non-empty text."; return false; } if (value.Length > max) { error = $"{name} exceeds {max} characters."; return false; } return true; }
    private static bool TryOptionalText(JsonElement root, string name, int max, out string? value, out string error) { value = null; error = string.Empty; if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null) return true; if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value = element.GetString()!.Trim())) { error = $"{name} must be null or non-empty text."; return false; } if (value.Length > max) { error = $"{name} exceeds {max} characters."; return false; } return true; }
    private static bool TryNullableLabel(JsonElement root, string name, IReadOnlyList<string> allowed, out string? value, out string error) { value = null; error = string.Empty; if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null) return true; if (element.ValueKind != JsonValueKind.String || !allowed.Contains(value = element.GetString()!, StringComparer.Ordinal)) { error = $"{name} is invalid."; return false; } return true; }
    private static bool TryUtc(JsonElement root, string name, out DateTime value, out string error) { value = default; error = string.Empty; if (!TryString(root, name, out var raw) || !raw.EndsWith('Z') || !DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value)) { error = $"{name} must be a UTC instant."; return false; } value = DateTime.SpecifyKind(value, DateTimeKind.Utc); return true; }
    private static bool TryEvidence(JsonElement root, string name, string kind, out List<AiEvidenceItem> items, out string error) { items = []; error = string.Empty; if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) { error = $"{name} must be an array."; return false; } if (array.GetArrayLength() > 10) { error = $"{name} exceeds 10 items."; return false; } foreach (var element in array.EnumerateArray()) { if (element.ValueKind != JsonValueKind.Object || !TryRequiredText(element, "text", 500, out var text, out error) || !element.TryGetProperty("sourceOrdinals", out var ordinals) || ordinals.ValueKind != JsonValueKind.Array) { error = error.Length == 0 ? $"{name} contains an invalid evidence item." : error; return false; } var numbers = new List<int>(); foreach (var ordinal in ordinals.EnumerateArray()) { if (ordinal.ValueKind != JsonValueKind.Number || !ordinal.TryGetInt32(out var number) || number < 0 || (numbers.Count > 0 && number <= numbers[^1])) { error = $"{name} sourceOrdinals must be ascending unique non-negative integers."; return false; } numbers.Add(number); } items.Add(new AiEvidenceItem(kind, text, numbers)); } return true; }
    private static bool TrySources(JsonElement root, DateTime checkedAtUtc, out List<AiSource> sources, out string error) { sources = []; error = string.Empty; if (!root.TryGetProperty("sources", out var array) || array.ValueKind != JsonValueKind.Array) { error = "sources must be an array."; return false; } if (array.GetArrayLength() > 20) { error = "sources exceeds 20 items."; return false; } var identities = new HashSet<string>(StringComparer.Ordinal); foreach (var element in array.EnumerateArray()) { if (element.ValueKind != JsonValueKind.Object || !TryRequiredText(element, "url", 2048, out var url, out error) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(uri.UserInfo) || !TryOptionalText(element, "title", 500, out var title, out error) || !TryUtc(element, "retrievedAtUtc", out var retrievedAtUtc, out error)) { error = error.Length == 0 ? "sources contains an invalid source." : error; return false; } DateTime? publishedAtUtc = null; if (element.TryGetProperty("publishedAtUtc", out var published) && published.ValueKind != JsonValueKind.Null) { if (!TryUtc(element, "publishedAtUtc", out var parsed, out error)) return false; publishedAtUtc = parsed; } if (publishedAtUtc > retrievedAtUtc || retrievedAtUtc > checkedAtUtc) { error = "source timestamps must satisfy published <= retrieved <= checked."; return false; } var identity = url + "\n" + (publishedAtUtc?.ToString("O") ?? string.Empty); if (!identities.Add(identity)) { error = "sources contains a duplicate URL revision."; return false; } sources.Add(new AiSource(url, title, publishedAtUtc, retrievedAtUtc)); } return true; }
    private static bool CitationsAreValid(IEnumerable<AiEvidenceItem> items, int sourceCount, out string error) { foreach (var item in items) if (item.SourceOrdinals.Any(ordinal => ordinal >= sourceCount)) { error = "An evidence citation refers to a missing source."; return false; } error = string.Empty; return true; }
}

public sealed record AiResultV1(string Outcome, string? Verdict, string? Confidence, string Summary, string? TechnicalView, string? FundamentalView, IReadOnlyList<AiEvidenceItem> PositiveFactors, IReadOnlyList<AiEvidenceItem> RiskFactors, IReadOnlyList<AiEvidenceItem> InvalidationConditions, DateTime CheckedAtUtc, IReadOnlyList<AiSource> Sources);
public sealed record AiEvidenceItem(string Kind, string Text, IReadOnlyList<int> SourceOrdinals);
public sealed record AiSource(string Url, string? Title, DateTime? PublishedAtUtc, DateTime RetrievedAtUtc);
public sealed record AiResultParseResult(AiResultV1? Value, string? StructuredResultSha256, string? ErrorKind, string? ErrorDetail) { public bool IsValid => Value is not null; public static AiResultParseResult Valid(AiResultV1 value, string hash) => new(value, hash, null, null); public static AiResultParseResult Invalid(string kind, string detail) => new(null, null, kind, detail); }
