using System.Text.Json;

namespace SwingAdviser.Domain.Analysis;

public sealed record TechnicalStrategyParameters(
    int EmaShortPeriod = 20,
    int EmaMediumPeriod = 50,
    int EmaLongPeriod = 200,
    int MacdFastPeriod = 12,
    int MacdSlowPeriod = 26,
    int MacdSignalPeriod = 9,
    int AtrPeriod = 14,
    int VolumeAveragePeriod = 20,
    decimal LongMinimumVolumeRatio = 1.5m,
    decimal ShortMinimumVolumeRatio = 1.5m,
    decimal ShortFullStrengthVolumeRatio = 2m,
    decimal AtrNormalizationScale = 1m,
    decimal MatchedBaseFraction = .5m)
{
    public const string StrategyKey = "candidate-scoring";
    public const string StrategyVersion = "v1";
    public const string IndicatorEngineVersion = "technical-indicators-v1";
    public const string CandidateEngineVersion = "candidate-scoring-engine-v1";
    public int RequiredHistoryCount => EmaLongPeriod + 1;

    public void Validate()
    {
        if (EmaShortPeriod <= 0 || EmaMediumPeriod <= EmaShortPeriod || EmaLongPeriod <= EmaMediumPeriod || MacdFastPeriod <= 0 || MacdSlowPeriod <= MacdFastPeriod || MacdSignalPeriod <= 0 || AtrPeriod <= 0 || VolumeAveragePeriod <= 0 || LongMinimumVolumeRatio <= 0 || ShortMinimumVolumeRatio <= 0 || ShortFullStrengthVolumeRatio <= ShortMinimumVolumeRatio || AtrNormalizationScale <= 0 || MatchedBaseFraction is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(TechnicalStrategyParameters), "Technical strategy parameters are invalid.");
    }
}

public sealed record AdjustedDailyBar(int DailyBarId, DateOnly TradingDate, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

public sealed record PointInTimeAnalysisSeries(int ManifestId, int InstrumentId, DateOnly EvaluationBarDate, DateTime AnalyzedAtUtc, string ManifestHash, string PriceRevisionSetHash, string CorporateActionSetHash, IReadOnlyList<AdjustedDailyBar> Bars, string DataStatus, int HistoryRequiredCount);

public sealed record IndicatorComputation(
    string DataStatus, int HistoryAvailableCount, int HistoryRequiredCount,
    decimal? MacdLine, decimal? MacdSignal, decimal? MacdHistogram, decimal? Ema20, decimal? Ema50, decimal? Ema200,
    decimal? Atr14, decimal? VolumeRatio, decimal? VolumeReferenceAverage, string VolumeRatioStatus, string RawValuesJson,
    decimal? PreviousMacdLine, decimal? PreviousMacdSignal, decimal? PreviousEma20, decimal? PreviousEma50, decimal? PreviousEma200);

public sealed record CandidateEvaluation(string Direction, bool Matched, int? Score, string? ConfidenceLabel, string ComponentsJson);

public sealed class TechnicalIndicatorEngine
{
    public IndicatorComputation Calculate(PointInTimeAnalysisSeries series, TechnicalStrategyParameters parameters)
    {
        parameters.Validate();
        if (series.DataStatus != "Ok") return Empty(series.DataStatus, series.Bars.Count, parameters.RequiredHistoryCount);
        if (series.Bars.Count < parameters.RequiredHistoryCount) return Empty("InsufficientHistory", series.Bars.Count, parameters.RequiredHistoryCount);
        if (!IsValid(series)) return Empty("InvalidData", series.Bars.Count, parameters.RequiredHistoryCount);

        var closes = series.Bars.Select(bar => bar.Close).ToArray();
        var ema20 = Ema(closes, parameters.EmaShortPeriod);
        var ema50 = Ema(closes, parameters.EmaMediumPeriod);
        var ema200 = Ema(closes, parameters.EmaLongPeriod);
        var fast = Ema(closes, parameters.MacdFastPeriod);
        var slow = Ema(closes, parameters.MacdSlowPeriod);
        var macd = new decimal?[closes.Length];
        for (var index = 0; index < closes.Length; index++) if (fast[index].HasValue && slow[index].HasValue) macd[index] = fast[index]!.Value - slow[index]!.Value;
        var signal = EmaNullable(macd, parameters.MacdSignalPeriod);
        var atr = Atr(series.Bars, parameters.AtrPeriod);
        var current = series.Bars.Count - 1;
        var reference = series.Bars.Skip(current - parameters.VolumeAveragePeriod).Take(parameters.VolumeAveragePeriod).Select(bar => (decimal)bar.Volume).Average();
        var volumeStatus = reference == 0 ? "ReferenceAverageZero" : "Ok";
        decimal? ratio = reference == 0 ? null : series.Bars[current].Volume / reference;
        var raw = JsonSerializer.Serialize(new { schemaVersion = "technical-indicator-raw-v1", manifestHash = series.ManifestHash, priceRevisionSetHash = series.PriceRevisionSetHash, corporateActionSetHash = series.CorporateActionSetHash, emaAlgorithm = "ema-sma-seed-v1", macdAlgorithm = "macd-ema-sma-seed-v1", atrAlgorithm = "atr-wilder-v1", volumeAverageAlgorithm = "volume-trailing-prior-bars-sma-v1", volumeRatioAlgorithm = "volume-current-to-prior-average-v1" });
        if (reference == 0) return new IndicatorComputation("InvalidData", series.Bars.Count, parameters.RequiredHistoryCount, macd[current], signal[current], macd[current] - signal[current], ema20[current], ema50[current], ema200[current], atr[current], null, reference, volumeStatus, raw, macd[current - 1], signal[current - 1], ema20[current - 1], ema50[current - 1], ema200[current - 1]);
        return new IndicatorComputation("Ok", series.Bars.Count, parameters.RequiredHistoryCount, macd[current], signal[current], macd[current] - signal[current], ema20[current], ema50[current], ema200[current], atr[current], ratio, reference, volumeStatus, raw, macd[current - 1], signal[current - 1], ema20[current - 1], ema50[current - 1], ema200[current - 1]);
    }

    private static IndicatorComputation Empty(string status, int available, int required) => new(status, available, required, null, null, null, null, null, null, null, null, null, "Missing", JsonSerializer.Serialize(new { schemaVersion = "technical-indicator-raw-v1", status }), null, null, null, null, null);
    private static bool IsValid(PointInTimeAnalysisSeries series) => series.Bars.Count > 0 && series.Bars[^1].TradingDate == series.EvaluationBarDate && series.Bars.Select((bar, index) => bar.DailyBarId > 0 && bar.Open > 0 && bar.High >= bar.Low && bar.Low > 0 && bar.Close >= bar.Low && bar.Close <= bar.High && bar.Volume >= 0 && (index == 0 || series.Bars[index - 1].TradingDate < bar.TradingDate) && bar.TradingDate <= series.EvaluationBarDate).All(valid => valid);
    private static decimal?[] Ema(decimal[] values, int period) => EmaNullable(values.Select(value => (decimal?)value).ToArray(), period);
    private static decimal?[] EmaNullable(decimal?[] values, int period)
    {
        var output = new decimal?[values.Length];
        for (var index = period - 1; index < values.Length; index++)
        {
            if (!values.Skip(index - period + 1).Take(period).All(value => value.HasValue)) continue;
            var seed = values.Skip(index - period + 1).Take(period).Select(value => value!.Value).Average();
            output[index] = index == period - 1 || !output[index - 1].HasValue ? seed : (2m / (period + 1)) * values[index]!.Value + (1m - 2m / (period + 1)) * output[index - 1]!.Value;
        }
        return output;
    }
    private static decimal?[] Atr(IReadOnlyList<AdjustedDailyBar> bars, int period)
    {
        var output = new decimal?[bars.Count]; var tr = new decimal[bars.Count];
        for (var index = 0; index < bars.Count; index++) tr[index] = index == 0 ? bars[index].High - bars[index].Low : new[] { bars[index].High - bars[index].Low, decimal.Abs(bars[index].High - bars[index - 1].Close), decimal.Abs(bars[index].Low - bars[index - 1].Close) }.Max();
        if (bars.Count < period) return output;
        output[period - 1] = tr.Take(period).Average();
        for (var index = period; index < bars.Count; index++) output[index] = (output[index - 1]!.Value * (period - 1) + tr[index]) / period;
        return output;
    }
}

public sealed class CandidateScoringEngine
{
    public CandidateEvaluation Evaluate(IndicatorComputation indicator, string direction, TechnicalStrategyParameters parameters)
    {
        if (indicator.DataStatus != "Ok" || indicator.VolumeRatioStatus != "Ok" || indicator.MacdLine is null || indicator.MacdSignal is null || indicator.Ema20 is null || indicator.Ema50 is null || indicator.Ema200 is null || indicator.Atr14 is null || indicator.VolumeRatio is null)
            return new CandidateEvaluation(direction, false, null, null, JsonSerializer.Serialize(new { schemaVersion = "candidate-score-components-v1", direction, reason = "InvalidData" }));
        var isLong = direction == "Long"; if (!isLong && direction != "Short") throw new ArgumentOutOfRangeException(nameof(direction));
        var macdGap = isLong ? indicator.MacdLine.Value - indicator.MacdSignal.Value : indicator.MacdSignal.Value - indicator.MacdLine.Value;
        var emaGap = isLong ? decimal.Min(indicator.Ema20.Value - indicator.Ema50.Value, indicator.Ema50.Value - indicator.Ema200.Value) : decimal.Min(indicator.Ema50.Value - indicator.Ema20.Value, indicator.Ema200.Value - indicator.Ema50.Value);
        var volumeMinimum = isLong ? parameters.LongMinimumVolumeRatio : parameters.ShortMinimumVolumeRatio;
        var matched = macdGap > 0 && emaGap > 0 && indicator.VolumeRatio.Value >= volumeMinimum;
        var previousMacdGap = isLong ? indicator.PreviousMacdLine.GetValueOrDefault() - indicator.PreviousMacdSignal.GetValueOrDefault() : indicator.PreviousMacdSignal.GetValueOrDefault() - indicator.PreviousMacdLine.GetValueOrDefault();
        var previousEmaGap = isLong ? decimal.Min(indicator.PreviousEma20.GetValueOrDefault() - indicator.PreviousEma50.GetValueOrDefault(), indicator.PreviousEma50.GetValueOrDefault() - indicator.PreviousEma200.GetValueOrDefault()) : decimal.Min(indicator.PreviousEma50.GetValueOrDefault() - indicator.PreviousEma20.GetValueOrDefault(), indicator.PreviousEma200.GetValueOrDefault() - indicator.PreviousEma50.GetValueOrDefault());
        var state = previousMacdGap > 0 && previousEmaGap > 0 ? "Continuation" : "Fresh";
        if (!matched) return new CandidateEvaluation(direction, false, null, null, JsonSerializer.Serialize(new { schemaVersion = "candidate-score-components-v1", direction, matched, macdGap, emaGap, volumeRatio = indicator.VolumeRatio, minimumVolumeRatio = volumeMinimum, state }));
        var macdStrength = Strength(macdGap, indicator.Atr14.Value, parameters.AtrNormalizationScale);
        var emaStrength = Strength(emaGap, indicator.Atr14.Value, parameters.AtrNormalizationScale);
        var volumeStrength = isLong ? 0m : decimal.Clamp((indicator.VolumeRatio.Value - volumeMinimum) / (parameters.ShortFullStrengthVolumeRatio - volumeMinimum), 0m, 1m);
        var macdWeight = isLong ? 50m : 40m; var emaWeight = isLong ? 50m : 40m; var volumeWeight = isLong ? 0m : 20m;
        var factor = static (decimal strength, decimal baseFraction) => baseFraction + (1m - baseFraction) * strength;
        var awarded = macdWeight * factor(macdStrength, parameters.MatchedBaseFraction) + emaWeight * factor(emaStrength, parameters.MatchedBaseFraction) + volumeWeight * factor(volumeStrength, parameters.MatchedBaseFraction);
        var score = Math.Clamp(decimal.ToInt32(decimal.Round(awarded, 0, MidpointRounding.AwayFromZero)), 0, 100);
        var confidence = score >= 80 ? "High" : score >= 60 ? "Medium" : "Low";
        return new CandidateEvaluation(direction, true, score, confidence, JsonSerializer.Serialize(new { schemaVersion = "candidate-score-components-v1", direction, matched, state, macdGap, emaGap, atr = indicator.Atr14, volumeRatio = indicator.VolumeRatio, macdStrength, emaStrength, volumeStrength, macdWeight, emaWeight, volumeWeight, awarded }));
    }
    private static decimal Strength(decimal gap, decimal atr, decimal scale) => atr == 0 ? 1m : decimal.Clamp(gap / (gap + atr * scale), 0m, 1m);
}
