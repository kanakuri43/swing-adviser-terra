using SwingAdviser.Application.Analysis;

namespace SwingAdviser.Infrastructure.Tests;

public class AiResultV1ParserTests
{
    [Fact]
    public void Parse_SucceededResult_PreservesEvidenceOrderAndProducesHash()
    {
        var result = AiResultV1Parser.Parse("""
        {"schemaVersion":"ai-result-v1","outcome":"Succeeded","verdict":"Bullish","confidence":"High","summary":"根拠があります","technicalView":null,"fundamentalView":"安定","positiveFactors":[{"text":"上昇トレンド","sourceOrdinals":[0]}],"riskFactors":[],"invalidationConditions":[],"checkedAtUtc":"2026-08-31T01:05:00.0000000Z","sources":[{"url":"https://example.test/a","title":null,"publishedAtUtc":null,"retrievedAtUtc":"2026-08-31T01:00:00.0000000Z"}]}
        """);

        Assert.True(result.IsValid);
        Assert.Equal("Succeeded", result.Value!.Outcome);
        Assert.Equal("上昇トレンド", result.Value.PositiveFactors.Single().Text);
        Assert.Matches("^[0-9a-f]{64}$", result.StructuredResultSha256!);
    }

    [Fact]
    public void Parse_InsufficientInformation_IsNotConvertedToNeutral()
    {
        var result = AiResultV1Parser.Parse("""
        {"schemaVersion":"ai-result-v1","outcome":"InsufficientInformation","verdict":null,"confidence":null,"summary":"確認可能な根拠が不足しています","technicalView":null,"fundamentalView":null,"positiveFactors":[],"riskFactors":[],"invalidationConditions":[],"checkedAtUtc":"2026-08-31T01:05:00.0000000Z","sources":[]}
        """);

        Assert.True(result.IsValid);
        Assert.Equal("InsufficientInformation", result.Value!.Outcome);
        Assert.Null(result.Value.Verdict);
    }

    [Fact]
    public void Parse_RejectsUnsupportedSchemaAndInvalidInformationShortage()
    {
        var unsupported = AiResultV1Parser.Parse("{" + "\"schemaVersion\":\"ai-result-v2\"}");
        var invalidShortage = AiResultV1Parser.Parse("""
        {"schemaVersion":"ai-result-v1","outcome":"InsufficientInformation","verdict":"Neutral","confidence":"Low","summary":"不足","technicalView":null,"fundamentalView":null,"positiveFactors":[],"riskFactors":[],"invalidationConditions":[],"checkedAtUtc":"2026-08-31T01:05:00.0000000Z","sources":[]}
        """);

        Assert.False(unsupported.IsValid);
        Assert.Equal("UnsupportedResponse", unsupported.ErrorKind);
        Assert.False(invalidShortage.IsValid);
    }
}
