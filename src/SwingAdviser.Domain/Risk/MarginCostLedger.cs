using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Domain.Risk;

public sealed record MarginCostResolution(
    string Status,
    decimal? NetCost,
    bool IsResolved,
    IReadOnlyList<MarginCostLedgerEntry> SelectedLeaves,
    decimal? ConfirmedNetCost = null,
    bool HasCompleteConfirmedCost = false);
public sealed record LotProfitAndLoss(decimal PriceProfitLoss, decimal? ConfirmedCostAdjustedProfitLoss, decimal? ReferenceNetProfitLoss, decimal? CostToRiskRatio, MarginCostResolution CostResolution);

/// <summary>Resolves append-only cost revisions without converting missing data into zero.</summary>
public sealed class MarginCostLedger
{
    public MarginCostResolution Resolve(IEnumerable<MarginCostLedgerEntry> entries)
    {
        var all = entries?.ToArray() ?? Array.Empty<MarginCostLedgerEntry>();
        if (all.Length == 0) return new MarginCostResolution("Missing", null, false, all);
        var leaves = new List<MarginCostLedgerEntry>();
        var confirmedCosts = new List<decimal>();
        var allConfirmed = true;
        foreach (var group in all.GroupBy(entry => (entry.MarginLotId, entry.CostType, entry.PeriodStart, entry.PeriodEnd)))
        {
            // A revision chain points from newer entry to its superseded predecessor, so a leaf has no newer entry superseding it.
            var superseded = group.Where(entry => entry.SupersedesId.HasValue).Select(entry => entry.SupersedesId!.Value).ToHashSet();
            var candidates = group.Where(entry => !superseded.Contains(entry.LedgerEntryId)).ToArray();
            if (candidates.Length != 1) return new MarginCostResolution("InvalidData", null, false, leaves);
            var preferred = candidates[0];
            leaves.Add(preferred);
            if (preferred.Status is "Confirmed" or "Corrected" or "KnownZero" or "NotOccurred" or "NotApplicable")
            {
                if (preferred.Direction is not "Charge" and not "Credit") return new MarginCostResolution("InvalidData", null, false, leaves);
                confirmedCosts.Add(SignedAmount(preferred));
            }
            else allConfirmed = false;
        }
        if (leaves.Any(entry => entry.Status is "Unknown" or "Unpublished" || (entry.Amount is null && entry.Status is not "KnownZero" and not "NotOccurred" and not "NotApplicable") || entry.Direction is not "Charge" and not "Credit"))
            return new MarginCostResolution("Unresolved", null, false, leaves, allConfirmed ? confirmedCosts.Sum() : null, allConfirmed);
        var net = leaves.Sum(SignedAmount);
        return new MarginCostResolution("Resolved", net, true, leaves, allConfirmed ? confirmedCosts.Sum() : null, allConfirmed);
    }

    private static decimal SignedAmount(MarginCostLedgerEntry entry) => (entry.Direction == "Charge" ? 1m : -1m) * entry.Amount.GetValueOrDefault();
}

public sealed class LotProfitAndLossCalculator
{
    public LotProfitAndLoss Calculate(string side, decimal currentQuantity, decimal entryPrice, decimal currentPrice, decimal oneShareRisk, string currency, string entryCurrency, string currentCurrency, MarginCostResolution costs)
    {
        if ((side != RiskVocabulary.Long && side != RiskVocabulary.Short) || currentQuantity <= 0 || entryPrice <= 0 || currentPrice <= 0 || oneShareRisk <= 0 ||
            string.IsNullOrWhiteSpace(currency) || currency != entryCurrency || currency != currentCurrency)
            throw new ArgumentException("P&L requires matching non-empty currency and valid current-unit prices, quantity, and risk.");
        var priceProfit = (side == RiskVocabulary.Long ? currentPrice - entryPrice : entryPrice - currentPrice) * currentQuantity;
        decimal? confirmedCostAdjusted = costs.HasCompleteConfirmedCost ? priceProfit - costs.ConfirmedNetCost!.Value : null;
        decimal? costAdjusted = costs.IsResolved ? priceProfit - costs.NetCost!.Value : null;
        decimal? ratio = costs.IsResolved ? costs.NetCost!.Value / (oneShareRisk * currentQuantity) : null;
        return new LotProfitAndLoss(priceProfit, confirmedCostAdjusted, costAdjusted, ratio, costs);
    }
}
